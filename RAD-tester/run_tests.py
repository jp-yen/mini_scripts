#!/usr/bin/env python3
"""
run_tests.py - RADIUS 異常値・過酷ファジング耐性テスト自動実行スクリプト
mock_radius_server.py の応答生成エンジンを使用し、全 25 ケースを自動検証します。
"""

import sys
sys.dont_write_bytecode = True
import os
import subprocess
import time
import socket
import ssl
import struct
import threading
import re
import unicodedata

def str_width(s):
    """東アジア文字幅 (全角=2, 半角=1) を考慮した表示幅を計算します。"""
    w = 0
    for ch in str(s):
        ea = unicodedata.east_asian_width(ch)
        w += 2 if ea in ('W', 'F') else 1
    return w

def pad_str(s, width, align='left'):
    """表示幅に基づき適切にパディングした文字列を返します。"""
    s = str(s)
    current_w = str_width(s)
    pad = max(0, width - current_w)
    if align == 'right':
        return ' ' * pad + s
    elif align == 'center':
        left = pad // 2
        right = pad - left
        return ' ' * left + s + ' ' * right
    else:
        return s + ' ' * pad

from mock_radius_server import generate_response, ensure_test_cert, SECRET

PORT = 1812
RADSEC_PORT = 2083

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
CLIENT_EXE = os.path.join(SCRIPT_DIR, "RadiusTest.exe")

def get_server_ip():
    if os.name == 'nt':
        return "127.0.0.1"
    try:
        out = subprocess.check_output(["ip", "-4", "addr", "show", "eth0"]).decode()
        m = re.search(r"inet\s+(\d+\.\d+\.\d+\.\d+)", out)
        if m:
            return m.group(1)
    except Exception:
        pass
    return "127.0.0.1"

SERVER_IP = get_server_ip()

class MockServerThread(threading.Thread):
    def __init__(self, mode, port=PORT):
        super().__init__()
        self.mode = mode
        self.port = port
        self.running = True
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind(("0.0.0.0", self.port))
        self.sock.settimeout(0.2)

    def run(self):
        while self.running:
            try:
                data, addr = self.sock.recvfrom(8192)
                if len(data) >= 20:
                    resp = generate_response(self.mode, data, verbose=False)
                    if resp:
                        self.sock.sendto(resp, addr)
            except socket.timeout:
                continue
            except Exception:
                break

    def stop(self):
        self.running = False
        try:
            self.sock.close()
        except Exception:
            pass

class MockRadSecServerThread(threading.Thread):
    def __init__(self, mode, port=RADSEC_PORT, cert_file="test_cert.pem", key_file="test_key.pem"):
        super().__init__()
        self.mode = mode
        self.port = port
        self.running = True
        ensure_test_cert(cert_file, key_file)
        self.context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        self.context.load_cert_chain(certfile=cert_file, keyfile=key_file)
        self.context.minimum_version = ssl.TLSVersion.TLSv1_2
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind(("0.0.0.0", self.port))
        self.sock.listen(5)
        self.sock.settimeout(0.5)

    def run(self):
        while self.running:
            try:
                client_sock, addr = self.sock.accept()
            except socket.timeout:
                continue
            except Exception:
                break

            try:
                with self.context.wrap_socket(client_sock, server_side=True) as tls_conn:
                    tls_conn.settimeout(2.0)
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
                        resp = generate_response(self.mode, req_data, verbose=False)
                        if resp:
                            tls_conn.sendall(resp)
            except Exception:
                pass

    def stop(self):
        self.running = False
        try:
            self.sock.close()
        except Exception:
            pass

TEST_CASES = [
    # --- 1. 正常系 ---
    {
        "name": "正常系: Access-Accept",
        "mode": "valid_accept",
        "cmd_extra": [],
        "expect_rc": [0],
        "desc": "正規パケットの受領と正常終了"
    },
    {
        "name": "正常系: Access-Reject",
        "mode": "valid_reject",
        "cmd_extra": [],
        "expect_rc": [1],
        "desc": "認証拒否パケットの正常ハンドリング"
    },

    # --- 2. ヘッダ長・Length 境界異常 ---
    {
        "name": "異常系: 短小パケット (10 バイト)",
        "mode": "short_packet_10",
        "cmd_extra": [],
        "expect_rc": [5],
        "desc": "ヘッダ長未満(20B未満)のパケットを安全に検知してエラー終了"
    },
    {
        "name": "異常系: 極小パケット (3 バイト)",
        "mode": "short_packet_3",
        "cmd_extra": [],
        "expect_rc": [5],
        "desc": "3バイトの不正パケットを安全に検知してエラー終了"
    },
    {
        "name": "異常系: Length フィールド過大詐称 (1000B)",
        "mode": "length_overflow",
        "cmd_extra": [],
        "expect_rc": [0],
        "expect_out": "Malformed",
        "desc": "実長を超えるLength指定でもオーバーフローせず継続解析 (Malformed検知)"
    },
    {
        "name": "異常系: Length フィールド過小 (10B < 20B)",
        "mode": "length_underflow",
        "cmd_extra": [],
        "expect_rc": [0],
        "expect_out": "Malformed",
        "desc": "最小長未満のLength指定でも安全に継続解析 (Malformed検知)"
    },
    {
        "name": "過酷試験: RFC上限超過 5000B 巨大パケット",
        "mode": "jumbo_packet_5000",
        "cmd_extra": [],
        "expect_rc": [0],
        "expect_out": "Malformed",
        "desc": "4096B上限を超える5000B UDPパケットでWSAEMSGSIZEクラッシュせずMalformed検知"
    },

    # --- 3. 属性・VSA 境界異常 & 高負荷スパム ---
    {
        "name": "異常系: 属性の途切れ・切り詰め (attr_truncated)",
        "mode": "attr_truncated",
        "cmd_extra": [],
        "expect_rc": [0],
        "expect_out": "Malformed",
        "desc": "属性長 > バッファ残量のフォーマット異常を検知・警告して継続"
    },
    {
        "name": "異常系: 属性長ゼロ (attr_zero_len)",
        "mode": "attr_zero_len",
        "cmd_extra": [],
        "expect_rc": [0],
        "expect_out": "Malformed",
        "desc": "属性長 < 2 バイトのフォーマット異常を検知・警告して継続"
    },
    {
        "name": "異常系: Microsoft VSA サブデータ破損",
        "mode": "vsa_truncated",
        "cmd_extra": ["-mschapv2"],
        "expect_rc": [0],
        "desc": "VSAサブ長過大時のバッファオーバーフローを防止し警告付きで解析継続"
    },
    {
        "name": "異常系: VSA 長さヘッダ不正 (vsa_invalid_len)",
        "mode": "vsa_invalid_len",
        "cmd_extra": [],
        "expect_rc": [0],
        "desc": "VSAヘッダ長 < 6 バイト時の短小VSA警告と安全なパース継続"
    },
    {
        "name": "過酷試験: 極小属性 1500 個スパム",
        "mode": "attr_spam_1500",
        "cmd_extra": [],
        "expect_rc": [0],
        "desc": "1パケット内に1500個の属性が詰め込まれた高密度パケットを高速・安全に処理"
    },
    {
        "name": "過酷試験: 不正 UTF-8・NULL・制御文字混入",
        "mode": "invalid_utf8_control",
        "cmd_extra": [],
        "expect_rc": [0],
        "desc": "不正UTF-8バイト列やNULL文字・エスケープ文字でデコード例外を起こさず処理"
    },

    # --- 4. EAP 異常系 & フラグメント爆弾 ---
    {
        "name": "異常系: EAP-Message パケット長過大詐称",
        "mode": "eap_truncated",
        "cmd_extra": ["-peap", "-insecure"],
        "expect_rc": [4],
        "desc": "EAP パケット長過大時、境界安全に検知しEAP失敗(4)として正常復帰"
    },
    {
        "name": "異常系: EAP Type フィールド欠損 (Length=4)",
        "mode": "eap_no_type",
        "cmd_extra": ["-peap", "-insecure"],
        "expect_rc": [4],
        "desc": "Type欠損の不正 EAP パケットを安全に検知しEAP失敗(4)として正常復帰"
    },
    {
        "name": "過酷試験: EAP Flags バイト欠落 (Length=5)",
        "mode": "eap_empty_flags",
        "cmd_extra": ["-peap", "-insecure"],
        "expect_rc": [4],
        "expect_out": "Flags フィールド欠落",
        "desc": "Type直後のFlagsバイトが0バイトのEAPパケットでIndexOutOfRangeにならず安全終了"
    },
    {
        "name": "異常系: PEAP Start フラグ長欠損",
        "mode": "eap_peap_start_corrupt",
        "cmd_extra": ["-peap", "-insecure"],
        "expect_rc": [4],
        "desc": "Length フラグあり・データ欠損時の安全なエラー復帰"
    },
    {
        "name": "過酷試験: EAP More-Fragments 無限継続爆弾",
        "mode": "eap_fragment_bomb",
        "cmd_extra": ["-peap", "-insecure"],
        "expect_rc": [4],
        "expect_out": "上限 (64 回) を超えました",
        "desc": "Moreフラグを無限に返し続けるサーバーに対し64回上限で遮断し無限ループ・DoSを防止"
    },
    {
        "name": "過酷試験: EAP TLS 宣言長 2GB 詐称爆弾",
        "mode": "eap_declared_len_bomb",
        "cmd_extra": ["-peap", "-insecure"],
        "expect_rc": [4],
        "expect_out": "安全上限 (65536 バイト) を超えています",
        "desc": "TLSレコード長に2GB(0x7FFFFFFF)を宣言する攻撃を即座に検知しメモリ枯渇を防止"
    },

    # --- 5. Authenticator / BlastRADIUS 署名異常 & 未知コード ---
    {
        "name": "異常系: Response Authenticator 不一致",
        "mode": "bad_auth",
        "cmd_extra": [],
        "expect_rc": [0],
        "expect_out": "Response Authenticator 検証不一致",
        "desc": "Authenticator不一致を受信時に警告報告しつつ試験を継続"
    },
    {
        "name": "過酷試験: Message-Authenticator 改ざん",
        "mode": "bad_msg_auth",
        "cmd_extra": [],
        "expect_rc": [0],
        "expect_out": "Message-Authenticator 検証不一致",
        "desc": "Type 80 HMAC-MD5 署名の改ざんを確実に検知して警告報告"
    },
    {
        "name": "過酷試験: Message-Authenticator 長さ異常 (Len=6)",
        "mode": "invalid_msg_auth_len",
        "cmd_extra": [],
        "expect_rc": [0],
        "expect_out": "Message-Authenticator 検証不一致",
        "desc": "Type 80 の長さが18B以外の不正サイズ時に素通りさせず検証不一致として検知"
    },
    {
        "name": "過酷試験: Message-Authenticator 重複付与",
        "mode": "duplicate_msg_auth",
        "cmd_extra": [],
        "expect_rc": [0],
        "expect_out": "Message-Authenticator 検証不一致",
        "desc": "Type 80 が複数個含まれる BlastRADIUS 異常パケットを検証不一致として検知"
    },
    {
        "name": "異常系: 識別子 (ID) 不一致",
        "mode": "id_mismatch",
        "cmd_extra": [],
        "expect_rc": [0],
        "expect_out": "識別子 (Identifier) 不一致",
        "desc": "ID不一致を受信時に警告報告しつつ試験を継続"
    },
    {
        "name": "過酷試験: 未定義 RADIUS Code (Code=99)",
        "mode": "unknown_code_99",
        "cmd_extra": [],
        "expect_rc": [1],
        "expect_out": "想定外の応答コードを受信しました: 99",
        "desc": "未知のRADIUSコード(99)受信時にクラッシュせず警告を出して終了コード1で復帰"
    },
    # --- 8. RadSec (RADIUS over TLS 1.3 / 1.2 / RFC 6614) ---
    {
        "name": "RadSec: 正常系 Access-Accept (TLS 1.3)",
        "mode": "valid_accept",
        "proto": "radsec",
        "cmd_extra": ["-insecure"],
        "expect_rc": [0],
        "expect_out": "認証成功: Access-Accept",
        "desc": "RadSec (TCP/TLS 1.3) での正常な Access-Accept 受領とセッション確立"
    },
    {
        "name": "RadSec: 正常系 Access-Reject (TLS 1.3)",
        "mode": "valid_reject",
        "proto": "radsec",
        "cmd_extra": ["-insecure"],
        "expect_rc": [1],
        "expect_out": "認証拒否: Access-Reject",
        "desc": "RadSec (TCP/TLS 1.3) での正常な Access-Reject ハンドリング"
    },
    {
        "name": "RadSec: CA証明書明示検証 (-cacert)",
        "mode": "valid_accept",
        "proto": "radsec",
        "host": "localhost",
        "cmd_extra": ["-cacert", "test_cert.pem"],
        "expect_rc": [0],
        "expect_out": "認証成功: Access-Accept",
        "desc": "-insecure なしで -cacert によるルート検証を通過し認証成功"
    },
    {
        "name": "RadSec: TLS 1.2 強制ハンドシェイク (-tls 1.2)",
        "mode": "valid_accept",
        "proto": "radsec",
        "cmd_extra": ["-tls", "1.2", "-insecure"],
        "expect_rc": [0],
        "expect_out": "TLS ハンドシェイク完了: Tls12",
        "desc": "TLS 1.2 プロトコル指定での RadSec ハンドシェイクと認証成功"
    },
    {
        "name": "RadSec: 自己署名証明書の検証拒絶 (Untrusted CA)",
        "mode": "valid_accept",
        "proto": "radsec",
        "cmd_extra": [],
        "expect_rc": [3],
        "expect_out": "TLS ハンドシェイクに失敗しました",
        "desc": "-insecure や -cacert を指定しない場合、未信頼の自己署名証明書を安全に拒絶して終了"
    }
]

def main():
    print("=" * 78)
    print(f" RADIUS & RadSec クライアント 異常系・過酷ファジング耐性テスト (全 {len(TEST_CASES)} ケース)")
    print(f" 実行環境: {'Windows' if os.name == 'nt' else 'WSL/Linux'} (Python {sys.version.split()[0]})")
    print(f" クライアント: {CLIENT_EXE}")
    print(f" 対象サーバー: UDP={SERVER_IP}:{PORT}, RadSec=TCP/TLS {SERVER_IP}:{RADSEC_PORT}")
    print("=" * 78)

    results = []
    all_passed = True

    for i, tc in enumerate(TEST_CASES, 1):
        name = tc["name"]
        mode = tc["mode"]
        cmd_extra = tc["cmd_extra"]
        expect_rc = tc["expect_rc"]
        expect_out = tc.get("expect_out")
        desc = tc["desc"]
        proto = tc.get("proto", "udp")
        is_radsec = (proto == "radsec")

        print(f"\n[{i}/{len(TEST_CASES)}] テスト: {name} ({mode})")
        print(f"    説明: {desc}")

        # 1. サーバー起動
        if is_radsec:
            target_port = tc.get("port", RADSEC_PORT)
            server = MockRadSecServerThread(mode, port=target_port)
            server.start()
            time.sleep(0.08)
            server_host = tc.get("host", SERVER_IP)
            args = [server_host, "testing123", "user", "pass", "-radsec", "-p", str(target_port), "-t", "3", "-brief"] + cmd_extra
        else:
            server = MockServerThread(mode)
            server.start()
            time.sleep(0.05)
            args = [SERVER_IP, "testing123", "user", "pass", "-p", str(PORT), "-t", "2", "-r", "0", "-brief"] + cmd_extra

        # 2. クライアント引数構築
        cmd = [CLIENT_EXE] + args
        t0 = time.time()
        try:
            res = subprocess.run(cmd, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="replace", timeout=8)
            elapsed = time.time() - t0
            rc = res.returncode
            crashed = (rc < 0 or rc > 10)
            out_ok = (expect_out is None) or (expect_out in res.stdout)

            passed = (rc in expect_rc) and not crashed and out_ok
            status_str = "PASS" if passed else "FAIL"
            if not passed:
                all_passed = False

            print(f"    結果: [{status_str}] 終了コード={rc} (期待値: {expect_rc}), 所要時間={elapsed*1000:.1f}ms")
            warn_lines = [line.strip() for line in res.stdout.splitlines() if "[WARN]" in line or "警告" in line or "不一致" in line or "異常" in line or "FAIL" in line]
            for w in warn_lines[:2]:
                print(f"    報告内容: {w}")
            if not passed:
                if not out_ok:
                    print(f"    [!] 期待メッセージ未検出: '{expect_out}'")
                print(f"    STDOUT: {res.stdout.strip()[:300]}")
                print(f"    STDERR: {res.stderr.strip()[:300]}")
            results.append((name, status_str, rc, expect_rc, f"{elapsed*1000:.0f}ms"))

        except subprocess.TimeoutExpired:
            print("    結果: [FAIL] クライアントがハング/タイムアウトしました")
            results.append((name, "TIMEOUT", -1, expect_rc, ">8000ms"))
            all_passed = False
        finally:
            server.stop()
            server.join()
            time.sleep(0.03)

    col1_w = max(str_width('テスト名'), max(str_width(r[0]) for r in results)) + 2
    col2_w = 8   # 判定
    col3_w = 12  # 終了コード
    col4_w = 10  # 期待値
    col5_w = 8   # 時間

    total_w = col1_w + col2_w + col3_w + col4_w + col5_w + 12

    print("\n" + "=" * total_w)
    header = f"{pad_str('テスト名', col1_w)} | {pad_str('判定', col2_w, 'center')} | {pad_str('終了コード', col3_w, 'center')} | {pad_str('期待値', col4_w, 'center')} | {pad_str('時間', col5_w, 'right')}"
    print(header)
    print("-" * total_w)
    for name, status, rc, exp, el in results:
        line = f"{pad_str(name, col1_w)} | {pad_str(status, col2_w, 'center')} | {pad_str(rc, col3_w, 'center')} | {pad_str(str(exp), col4_w, 'center')} | {pad_str(el, col5_w, 'right')}"
        print(line)
    print("=" * total_w)

    if all_passed:
        print(f"[SUCCESS] 全 {len(TEST_CASES)} ケースの異常系・過酷ファジングテストに完全合格しました！")
        return 0
    else:
        print("[FAILURE] 一部のテストで期待と異なる挙動が確認されました。")
        return 1

if __name__ == "__main__":
    sys.exit(main())
