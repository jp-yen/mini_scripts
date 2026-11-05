#!/usr/bin/env python3
"""
mock_radius_server.py - RADIUS 模擬サーバー (異常系テスト・過酷ファジング対応)
様々な異常パケット・境界値パケット・プロトコル爆弾をクライアントに返信して耐性をテストします。
"""

import sys
import os
import socket
import ssl
import subprocess
import hashlib
import hmac
import argparse
import struct

SECRET = b"testing123"

def make_radius_response(code, req_id, req_auth, attrs=b"", secret=SECRET, corrupt_auth=False):
    pkt_len = 20 + len(attrs)
    # 1. ヘッダ生成 (Authenticator 部分は Request Authenticator を一時配置)
    header_for_hash = struct.pack("!BBH", code, req_id, pkt_len) + req_auth + attrs + secret
    resp_auth = hashlib.md5(header_for_hash).digest()
    if corrupt_auth:
        # 意図的にオーセンティケータを破壊
        resp_auth = bytes([b ^ 0xFF for b in resp_auth])
    return struct.pack("!BBH", code, req_id, pkt_len) + resp_auth + attrs

def get_req_eap_info(req_data):
    """リクエスト内の EAP-Message (Type 79) から (eap_id, eap_type) を抽出します。"""
    if len(req_data) < 20:
        return (1, 0)
    cur = 20
    while cur + 2 <= len(req_data):
        atype = req_data[cur]
        alen = req_data[cur + 1]
        if alen < 2 or cur + alen > len(req_data):
            break
        if atype == 79 and alen >= 7:
            eap_id = req_data[cur + 3]
            eap_type = req_data[cur + 6]
            return (eap_id, eap_type)
        cur += alen
    return (1, 0)

def generate_response(mode, req_data, verbose=True):
    if len(req_data) < 20:
        return None
    req_code, req_id, req_len = struct.unpack("!BBH", req_data[:4])
    req_auth = req_data[4:20]

    if verbose:
        print(f"[*] リクエスト受信: Code={req_code}, ID={req_id}, Len={req_len}, Mode={mode}")

    if mode == "valid_accept":
        # 正常な Access-Accept (Reply-Message 付き)
        msg = "Authentication Successful (Mock Server)".encode("utf-8")
        attr_reply = struct.pack("!BB", 18, 2 + len(msg)) + msg
        return make_radius_response(2, req_id, req_auth, attr_reply)

    elif mode == "valid_reject":
        # 正常な Access-Reject
        msg = "Authentication Rejected (Mock Server)".encode("utf-8")
        attr_reply = struct.pack("!BB", 18, 2 + len(msg)) + msg
        return make_radius_response(3, req_id, req_auth, attr_reply)

    elif mode == "short_packet_10":
        # パケット長 10 バイト (ヘッダ 20 バイト未満の短小異常)
        return b"\x02\x01\x00\x0a\x01\x02\x03\x04\x05\x06"

    elif mode == "short_packet_3":
        # 3 バイトの極小データ
        return b"\x02\x01\x00"

    elif mode == "length_overflow":
        # Length フィールドが 1000 バイトと詐称 (実データは 20 バイト)
        auth = hashlib.md5(b"dummy").digest()
        return struct.pack("!BBH", 2, req_id, 1000) + auth

    elif mode == "length_underflow":
        # Length フィールドが 10 バイト (< 20 バイト)
        auth = hashlib.md5(b"dummy").digest()
        return struct.pack("!BBH", 2, req_id, 10) + auth

    elif mode == "attr_truncated":
        # 属性長が 30 バイトとあるが、バッファ終端まで 5 バイトしかない
        bad_attr = struct.pack("!BB", 18, 30) + b"abc"
        return make_radius_response(2, req_id, req_auth, bad_attr)

    elif mode == "attr_zero_len":
        # 属性長フィールドが 0 (RFC 2865 では最低 2 バイト必要)
        bad_attr = struct.pack("!BB", 18, 0)
        return make_radius_response(2, req_id, req_auth, bad_attr)

    elif mode == "vsa_truncated":
        # Vendor 311 (Microsoft), SubType=2 (MS-CHAP-Error), SubLen=200 だが実データは 4 バイト
        vsa_data = struct.pack("!IBB", 311, 2, 200) + b"fail"
        vsa_attr = struct.pack("!BB", 26, 2 + len(vsa_data)) + vsa_data
        return make_radius_response(11, req_id, req_auth, vsa_attr)

    elif mode == "vsa_invalid_len":
        # VSA (Type 26) で属性値長が 4 バイト (< 6 バイトヘッダ)
        bad_vsa = struct.pack("!BB", 26, 6) + b"1234"
        return make_radius_response(2, req_id, req_auth, bad_vsa)

    elif mode == "eap_truncated":
        # EAP-Message (Type 79): EAP Length=100 だが実データは 4 バイト
        eap_data = struct.pack("!BBH", 1, 1, 100)
        eap_attr = struct.pack("!BB", 79, 2 + len(eap_data)) + eap_data
        return make_radius_response(11, req_id, req_auth, eap_attr)

    elif mode == "eap_no_type":
        # EAP-Message (Type 79): Code=1 (Request), Length=4 (Type フィールドなし)
        eap_data = struct.pack("!BBH", 1, 1, 4)
        eap_attr = struct.pack("!BB", 79, 2 + len(eap_data)) + eap_data
        return make_radius_response(11, req_id, req_auth, eap_attr)

    elif mode == "eap_empty_flags":
        # EAP-Message (Type 79): Code=1, ID=1, Length=5, Type=25(PEAP) だが Flags バイトが欠落 (Data=0バイト)
        eap_data = struct.pack("!BBHB", 1, 1, 5, 25)
        eap_attr = struct.pack("!BB", 79, 2 + len(eap_data)) + eap_data
        return make_radius_response(11, req_id, req_auth, eap_attr)

    elif mode == "eap_peap_start_corrupt":
        # PEAP Start (Type 25), Start + Length フラグ (0xA0) があるが 4バイトの Length フィールドが欠落
        eap_data = struct.pack("!BBHBB", 1, 1, 6, 25, 0xA0)
        eap_attr = struct.pack("!BB", 79, 2 + len(eap_data)) + eap_data
        return make_radius_response(11, req_id, req_auth, eap_attr)

    elif mode == "eap_fragment_bomb":
        # 1回目は正常な PEAP Start (0x20) を返し、2回目以降は More Fragments (0x40) を無限に返し続ける爆弾
        eap_id, eap_type = get_req_eap_info(req_data)
        if eap_type == 1:
            eap_data = struct.pack("!BBHBB", 1, (eap_id + 1) % 256, 6, 25, 0x20)
        else:
            chunk = b"\x16\x03\x03\x00\x20" + (b"X" * 27)
            eap_data = struct.pack("!BBHBB", 1, (eap_id + 1) % 256, 6 + len(chunk), 25, 0x40) + chunk
        eap_attr = struct.pack("!BB", 79, 2 + len(eap_data)) + eap_data
        return make_radius_response(11, req_id, req_auth, eap_attr)

    elif mode == "eap_declared_len_bomb":
        # 1回目は正常な PEAP Start (0x20)、2回目に TLS 宣言長 2GB (0x7FFFFFFF) を詐称するメモリ枯渇爆弾
        eap_id, eap_type = get_req_eap_info(req_data)
        if eap_type == 1:
            eap_data = struct.pack("!BBHBB", 1, (eap_id + 1) % 256, 6, 25, 0x20)
        else:
            eap_data = struct.pack("!BBHBBI", 1, (eap_id + 1) % 256, 18, 25, 0xC0, 0x7FFFFFFF) + (b"Z" * 8)
        eap_attr = struct.pack("!BB", 79, 2 + len(eap_data)) + eap_data
        return make_radius_response(11, req_id, req_auth, eap_attr)

    elif mode == "bad_auth":
        # 不正な Response Authenticator
        return make_radius_response(2, req_id, req_auth, corrupt_auth=True)

    elif mode == "bad_msg_auth":
        # Response Authenticator は正常だが、Message-Authenticator (Type 80) の HMAC-MD5 が改ざん
        bad_mac_attr = struct.pack("!BB", 80, 18) + (b"\xde\xad\xbe\xef" * 4)
        return make_radius_response(2, req_id, req_auth, bad_mac_attr)

    elif mode == "invalid_msg_auth_len":
        # Message-Authenticator (Type 80) の長さが 18 バイトではなく 6 バイトの異常サイズ
        bad_mac_attr = struct.pack("!BB", 80, 6) + b"1234"
        return make_radius_response(2, req_id, req_auth, bad_mac_attr)

    elif mode == "duplicate_msg_auth":
        # Message-Authenticator (Type 80) が 1 パケット内に 2 個存在する (BlastRADIUS 異常系)
        mac1 = struct.pack("!BB", 80, 18) + (b"\x00" * 16)
        mac2 = struct.pack("!BB", 80, 18) + (b"\x00" * 16)
        return make_radius_response(2, req_id, req_auth, mac1 + mac2)

    elif mode == "id_mismatch":
        # リクエストと異なる Identifier (ID + 1)
        return make_radius_response(2, (req_id + 1) % 256, req_auth)

    elif mode == "jumbo_packet_5000":
        # RFC 2865 上限 (4096 バイト) を超える 5000 バイトの巨大 UDP パケット
        # 250 バイト属性 (Type=18, Len=252) x 19 個 + 192 バイト属性 = 4980 バイト属性 + 20B ヘッダ = 5000 バイト
        attrs = (struct.pack("!BB", 18, 252) + (b"A" * 250)) * 19 + (struct.pack("!BB", 18, 192) + (b"B" * 190))
        return make_radius_response(2, req_id, req_auth, attrs)

    elif mode == "attr_spam_1500":
        # 1 パケット内に空の Reply-Message (Type=18, Len=2) を 1500 個詰め込んだ高密度スパム (計 3020 バイト)
        attrs = struct.pack("!BB", 18, 2) * 1500
        return make_radius_response(2, req_id, req_auth, attrs)

    elif mode == "invalid_utf8_control":
        # 不正な UTF-8 バイト列、NULL バイト、制御文字、ANSI エスケープを含む Reply-Message
        evil_bytes = b"Null:\x00 BadUTF8:\xff\xfe\x80\x81 Ctrl:\x01\x02\x07\x08 ANSI:\x1b[31mRED\x1b[0m\r\nNextLine"
        attr = struct.pack("!BB", 18, 2 + len(evil_bytes)) + evil_bytes
        return make_radius_response(2, req_id, req_auth, attr)

    elif mode == "unknown_code_99":
        # 未定義の RADIUS Code=99
        return make_radius_response(99, req_id, req_auth)

    else:
        print(f"[!] 未知のモード: {mode}")
        return make_radius_response(3, req_id, req_auth)

def ensure_test_cert(cert_file="test_cert.pem", key_file="test_key.pem"):
    if not os.path.exists(cert_file) or not os.path.exists(key_file):
        print(f"[*] テスト証明書が見つからないため自動生成します: {cert_file}, {key_file}")
        cli_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bin", "Debug", "net10.0-windows", "RadiusTest.exe")
        if os.path.exists(cli_path):
            subprocess.run([cli_path, "-gencert", cert_file, key_file], check=True)
        else:
            subprocess.run(["dotnet", "run", "--", "-gencert", cert_file, key_file], check=True)

def run_udp_server(port, mode, single_shot=False):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind(("0.0.0.0", port))
    print(f"[+] RADIUS/UDP 模擬サーバー起動: 0.0.0.0:{port} (モード: {mode})")
    sys.stdout.flush()

    try:
        while True:
            data, addr = sock.recvfrom(4096)
            resp = generate_response(mode, data)
            if resp:
                sock.sendto(resp, addr)
                print(f"[+] クライアント {addr} へ応答送信 ({len(resp)} バイト)")
                sys.stdout.flush()
            if single_shot:
                break
    except KeyboardInterrupt:
        print("\n[-] サーバー停止")
    finally:
        sock.close()

def run_radsec_server(port, mode, cert_file="test_cert.pem", key_file="test_key.pem", single_shot=False):
    ensure_test_cert(cert_file, key_file)

    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(certfile=cert_file, keyfile=key_file)
    context.minimum_version = ssl.TLSVersion.TLSv1_2

    raw_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    raw_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    raw_sock.bind(("0.0.0.0", port))
    raw_sock.listen(5)
    print(f"[+] RadSec (TLS) 模擬サーバー起動: 0.0.0.0:{port} (モード: {mode})")
    sys.stdout.flush()

    try:
        while True:
            client_sock, addr = raw_sock.accept()
            try:
                with context.wrap_socket(client_sock, server_side=True) as tls_conn:
                    print(f"[+] RadSec TLS ハンドシェイク成功: {addr} (プロトコル: {tls_conn.version()}, 暗号: {tls_conn.cipher()[0]})")
                    sys.stdout.flush()

                    hdr = b""
                    while len(hdr) < 4:
                        chunk = tls_conn.recv(4 - len(hdr))
                        if not chunk: break
                        hdr += chunk

                    if len(hdr) == 4:
                        pkt_len = struct.unpack("!H", hdr[2:4])[0]
                        body = b""
                        while len(body) < pkt_len - 4:
                            chunk = tls_conn.recv(pkt_len - 4 - len(body))
                            if not chunk: break
                            body += chunk

                        req_data = hdr + body
                        resp = generate_response(mode, req_data)
                        if resp:
                            tls_conn.sendall(resp)
                            print(f"[+] RadSec 応答送信 ({len(resp)} バイト)")
                            sys.stdout.flush()
            except Exception as ex:
                print(f"[!] RadSec 接続エラー ({addr}): {ex}")
                sys.stdout.flush()

            if single_shot:
                break
    except KeyboardInterrupt:
        print("\n[-] RadSec サーバー停止")
    finally:
        raw_sock.close()

def run_server(port, mode, single_shot=False):
    run_udp_server(port, mode, single_shot)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="RADIUS / RadSec 模擬サーバー")
    parser.add_argument("--port", type=int, default=None, help="待受ポート (既定: UDP=1812, RadSec=2083)")
    parser.add_argument("--mode", type=str, default="valid_accept", help="応答モード")
    parser.add_argument("--radsec", action="store_true", help="RadSec (TLS/TCP) モードで起動")
    parser.add_argument("--cert", type=str, default="test_cert.pem", help="証明書ファイル")
    parser.add_argument("--key", type=str, default="test_key.pem", help="秘密鍵ファイル")
    parser.add_argument("--single", action="store_true", help="1回応答後に終了")
    args = parser.parse_args()

    port = args.port if args.port is not None else (2083 if args.radsec else 1812)
    if args.radsec:
        run_radsec_server(port, args.mode, args.cert, args.key, args.single)
    else:
        run_udp_server(port, args.mode, args.single)
