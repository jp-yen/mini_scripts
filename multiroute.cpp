// g++ -std=c++17 -Wall -Wextra -pthread -o multiroute multiroute.cpp -ltins
#include <iostream>
#include <string>
#include <vector>
#include <thread>
#include <chrono>
#include <mutex>
#include <map>
#include <iomanip>
#include <csignal>
#include <atomic>
#include <condition_variable>
#include <memory>
#include <sstream>

// Tins and system headers
#include <tins/tins.h>
#include <unistd.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <netinet/ip.h> // IP_HDRINCL に必要
#include <arpa/inet.h>

using namespace Tins;

// ────────────────────────────────────────────
// 定数
// ────────────────────────────────────────────
static constexpr int    TIMESTAMP_WIDTH  = 12;
static constexpr int    HOP_WIDTH        = 15;
static constexpr int    TTL_DELAY_MS     = 30;  // 1hop ごとの送信オフセット [ms]
static constexpr int    RECV_BUFFER_SIZE = 1500;
static constexpr double TIMEOUT_RATIO    = 0.9; // interval の何割をタイムアウトにするか

// ────────────────────────────────────────────
// グローバル停止フラグ（シグナルハンドラと共有）
// ────────────────────────────────────────────
static std::atomic<bool> g_stop_requested{false};

// ────────────────────────────────────────────
// SocketGuard : RAIIによるソケット管理
// ────────────────────────────────────────────

/**
 * @brief raw ソケットの RAII ラッパー。
 *        スコープを抜けると自動的に close() する。
 */
class SocketGuard {
public:
    explicit SocketGuard(int fd) : fd_(fd) {}
    ~SocketGuard() { if (fd_ >= 0) close(fd_); }

    int  get()   const { return fd_; }
    bool valid() const { return fd_ >= 0; }

    // コピー禁止・ムーブのみ許可
    SocketGuard(const SocketGuard&)            = delete;
    SocketGuard& operator=(const SocketGuard&) = delete;
    SocketGuard(SocketGuard&& o) noexcept : fd_(o.fd_) { o.fd_ = -1; }
    SocketGuard& operator=(SocketGuard&&)      = delete;

private:
    int fd_;
};

// ────────────────────────────────────────────
// ユーティリティ関数
// ────────────────────────────────────────────


/**
 * @brief UTF-8文字列の端末表示幅を返す（ASCII=1カラム、CJK全角=2カラム）。
 */
static int display_width(const std::string& s) {
    int width = 0;
    for (size_t i = 0; i < s.size(); ) {
        const unsigned char c = static_cast<unsigned char>(s[i]);
        if      (c < 0x80) { width += 1; i += 1; } // ASCII
        else if (c < 0xE0) { width += 1; i += 2; } // 2バイト文字（Latin 等）
        else if (c < 0xF0) { width += 2; i += 3; } // 3バイト文字（CJK 等）
        else               { width += 2; i += 4; } // 4バイト文字
    }
    return width;
}



/**
 * @brief 表示幅が target_width になるよう両側スペースでセンタリングした文字列を返す。
 * 割り切れない場合は左側パディングを小さく、右側パディングを大きく取る。
 */
static std::string pad_center(const std::string& s, int target_width) {
    const int dw      = display_width(s);
    if (dw >= target_width) return s;
    const int total   = target_width - dw;
    const int left    = total / 2;
    const int right   = total - left;
    return std::string(left, ' ') + s + std::string(right, ' ');
}


/**
 * @brief 受信バッファからICMP応答のシーケンス番号を抽出する。
 *
 * TIME_EXCEEDED または ECHO_REPLY のうち、id が expected_id に一致するものを探す。
 * 一致した場合はそのシーケンス番号を返し、一致しなければ -1 を返す。
 *
 * @param buffer      受信バッファ
 * @param bytes       受信バイト数
 * @param expected_id 期待するICMP識別子（送信時に設定したTTL値）
 * @return int 一致したシーケンス番号、または -1
 */
static int extract_response_ttl(const char* buffer, int bytes, int expected_id) {
    IP received_ip(reinterpret_cast<const uint8_t*>(buffer), bytes);
    const ICMP* received_icmp = received_ip.find_pdu<ICMP>();
    if (!received_icmp) return -1;

    if (received_icmp->type() == ICMP::TIME_EXCEEDED) {
        // TIME_EXCEEDED の場合、内包された元パケットのヘッダを確認する
        const RawPDU* inner_raw = received_icmp->find_pdu<RawPDU>();
        if (!inner_raw) return -1;

        IP inner_ip(inner_raw->payload().data(), inner_raw->payload().size());
        const ICMP* inner_icmp = inner_ip.find_pdu<ICMP>();
        if (inner_icmp && inner_icmp->id() == static_cast<uint16_t>(expected_id)) {
            return inner_icmp->sequence();
        }
    } else if (received_icmp->type() == ICMP::ECHO_REPLY) {
        if (received_icmp->id() == static_cast<uint16_t>(expected_id)) {
            return received_icmp->sequence();
        }
    }
    return -1;
}

// ────────────────────────────────────────────
// ProbeWorker : TTL ごとの独立したプローブワーカー
// ────────────────────────────────────────────

/**
 * @brief 指定した TTL でICMPプローブを送り続け、応答元IPを記録するワーカー。
 *
 * 各ワーカーは独立したスレッドで動作し、interval ごとにプローブを送信する。
 * 送信タイミングは min_ttl からの差分に基づく初期オフセットでずらし、
 * パケットが同時に届くことによる競合を避ける。
 */
class ProbeWorker {
public:
    ProbeWorker(int ttl, IPv4Address dest, std::chrono::milliseconds interval,
                int min_ttl, const std::string& iface)
        : ttl_(ttl), min_ttl_(min_ttl), dest_addr_(dest), interval_(interval),
          iface_name_(iface), current_ip_("*"), running_(true) {}

    /** 現在記録されている応答元IPアドレスを返す（スレッドセーフ）。 */
    std::string get_ip() const {
        std::lock_guard<std::mutex> lock(mtx_);
        return current_ip_;
    }

    /**
     * @brief 今のサイクルのプローブを送信した時刻を "HH:MM:SS.mmm" 形式で返す。
     *        まだ送信前の場合は空白を返す。
     */
    std::string get_send_timestamp() const {
        const int64_t ms = send_time_ms_.load(std::memory_order_relaxed);
        if (ms == 0) return std::string(TIMESTAMP_WIDTH, ' ');
        const std::time_t t      = ms / 1000;
        const int         ms_rem = static_cast<int>(ms % 1000);
        std::ostringstream oss;
        oss << std::put_time(std::localtime(&t), "%H:%M:%S")
            << '.' << std::setfill('0') << std::setw(3) << ms_rem;
        return oss.str();
    }

    /** ワーカーの停止を要求する。 */
    void stop() { running_ = false; }

    /** スレッドのエントリポイント。 */
    void run() {
        // 初期オフセット待機：min_ttl からの差分でずらす
        const auto offset = std::chrono::milliseconds((ttl_ - min_ttl_) * TTL_DELAY_MS);
        std::this_thread::sleep_for(offset);

        // 待機中に別ワーカーがエラーで停止していた場合は何もしない
        if (g_stop_requested) return;

        SocketGuard sock{socket(AF_INET, SOCK_RAW, IPPROTO_ICMP)};
        if (!sock.valid()) return;

        // カーネルに IP ヘッダを自前で用意することを通知する
        int on = 1;
        if (setsockopt(sock.get(), IPPROTO_IP, IP_HDRINCL, &on, sizeof(on)) < 0) return;

        // 送信インタフェースを指定する（空の場合はカーネルが自動選択）
        if (!iface_name_.empty()) {
            if (setsockopt(sock.get(), SOL_SOCKET, SO_BINDTODEVICE,
                           iface_name_.c_str(), static_cast<socklen_t>(iface_name_.size())) < 0) {
                std::cerr << "エラー: インタフェース '" << iface_name_
                          << "' へのバインドに失敗しました。\n";
                g_stop_requested = true;
                return;
            }
        }

        // 受信タイムアウト = interval の TIMEOUT_RATIO 倍
        const long timeout_us = static_cast<long>(interval_.count() * 1000 * TIMEOUT_RATIO);
        struct timeval tv{0, timeout_us};
        setsockopt(sock.get(), SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));

        while (running_) {
            const auto cycle_start = std::chrono::steady_clock::now();

            // 前回の応答をリセット
            { std::lock_guard<std::mutex> lock(mtx_); current_ip_ = "*"; }

            send_probe(sock.get());
            receive_response(sock.get());

            // 残り時間だけスリープして周期を揃える
            const auto elapsed = std::chrono::steady_clock::now() - cycle_start;
            if (running_ && elapsed < interval_) {
                std::this_thread::sleep_for(interval_ - elapsed);
            }
        }
    }

private:
    /**
     * @brief ICMPプローブパケットを1つ送信する。
     * @param sock 送信に使用するソケットFD
     */
    void send_probe(int sock) {
        // sendto() の直前に送信時刻を記録する
        using namespace std::chrono;
        send_time_ms_.store(
            duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count(),
            std::memory_order_relaxed);

        try {
            IP ip = IP(dest_addr_) / ICMP();
            ICMP& icmp = ip.rfind_pdu<ICMP>();
            icmp.type(ICMP::ECHO_REQUEST);
            icmp.id(ttl_);
            icmp.sequence(ttl_);
            ip.ttl(ttl_);

            auto serialized = ip.serialize();
            struct sockaddr_in sin{};
            sin.sin_family      = AF_INET;
            sin.sin_port        = 0;
            sin.sin_addr.s_addr = dest_addr_;

            sendto(sock, serialized.data(), serialized.size(), 0,
                   reinterpret_cast<struct sockaddr*>(&sin), sizeof(sin));
        } catch (...) {
            // 送信エラーは無視して次のサイクルへ
        }
    }

    /**
     * @brief 自分宛の応答が届くまでソケットを読み続ける。
     *
     * タイムアウトまたは停止要求があればループを抜ける。
     * 応答が見つかれば current_ip_ を更新する。
     *
     * @param sock 受信に使用するソケットFD
     */
    void receive_response(int sock) {
        char buffer[RECV_BUFFER_SIZE];
        while (running_) {
            const int bytes = recvfrom(sock, buffer, sizeof(buffer), 0, nullptr, nullptr);
            if (bytes <= 0) break; // タイムアウトまたはエラー

            try {
                const int response_ttl = extract_response_ttl(buffer, bytes, ttl_);
                if (response_ttl == ttl_) {
                    IP received_ip(reinterpret_cast<const uint8_t*>(buffer), bytes);
                    std::lock_guard<std::mutex> lock(mtx_);
                    current_ip_ = received_ip.src_addr().to_string();
                    break;
                }
            } catch (...) {
                // パースエラーは無視して次のパケットへ
            }
        }
    }

    // ── メンバ変数 ──────────────────────────────
    const int ttl_;
    const int min_ttl_;
    const IPv4Address dest_addr_;
    const std::chrono::milliseconds interval_;
    const std::string iface_name_;

    std::string current_ip_;
    std::atomic<int64_t> send_time_ms_{0};  // 今のサイクルのプローブ送信時刻（エポックからのミリ秒）
    std::atomic<bool> running_;
    mutable std::mutex mtx_;
};

// ────────────────────────────────────────────
// MultiTracer : 全ワーカーの管理と定期的な結果表示
// ────────────────────────────────────────────

/**
 * @brief 指定されたTTL範囲でProbeWorkerを生成・管理し、
 *        一定間隔で全ホップの応答IPを表示するクラス。
 */
class MultiTracer {
public:
    MultiTracer(IPv4Address dest, int min_ttl, int max_ttl,
                std::chrono::milliseconds interval, const std::string& iface)
        : dest_addr_(dest), min_ttl_(min_ttl), max_ttl_(max_ttl),
          interval_(interval), iface_name_(iface) {
        for (int ttl = min_ttl; ttl <= max_ttl; ++ttl) {
            workers_.push_back(std::make_unique<ProbeWorker>(ttl, dest, interval, min_ttl, iface));
        }
    }

    /** 全ワーカーを起動し、interval ごとに結果を表示する。 */
    void run() {
        std::vector<std::thread> worker_threads;
        worker_threads.reserve(workers_.size());
        for (const auto& w : workers_) {
            worker_threads.emplace_back(&ProbeWorker::run, w.get());
        }

        // interval_/2 ずらして開始することで、ディスプレイがサイクルの中間点（応答受信後）に
        // 発火するようにする。こうすることで min_ttl ワーカーのリセット直後に表示が重なる
        // 競合状態を防ぐ。
        auto next_tick = std::chrono::steady_clock::now() + interval_ / 2;
        while (!g_stop_requested) {
            next_tick += interval_;
            std::this_thread::sleep_until(next_tick);
            if (g_stop_requested) break;
            print_results();
        }

        stop();
        for (auto& t : worker_threads) {
            if (t.joinable()) t.join();
        }
    }

    /** 全ワーカーに停止を要求する。 */
    void stop() {
        for (const auto& w : workers_) w->stop();
    }

private:
    /** min_ttl ワーカーの送信時刻と全ホップのIPを1行で標準出力に出力する。 */
    void print_results() const {
        // タイムスタンプは min_ttl ワーカー（workers_[0]）の送信時刻を使う
        std::cout << std::left << std::setw(TIMESTAMP_WIDTH) << workers_[0]->get_send_timestamp() << '|';
        for (const auto& w : workers_) {
            std::cout << std::left << std::setw(HOP_WIDTH) << w->get_ip() << '|';
        }
        std::cout << '\n';
    }

    // ── メンバ変数 ──────────────────────────────
    const IPv4Address dest_addr_;
    const int min_ttl_;
    const int max_ttl_;
    const std::chrono::milliseconds interval_;
    const std::string iface_name_;

    std::vector<std::unique_ptr<ProbeWorker>> workers_;
};

// ────────────────────────────────────────────
// シグナルハンドラ
// ────────────────────────────────────────────

static void signal_handler(int /*signum*/) {
    std::cout << "\n終了中...\n";
    g_stop_requested = true;
}

// ────────────────────────────────────────────
// 使い方の表示
// ────────────────────────────────────────────

static void print_usage(const char* prog_name) {
    std::cerr << "使い方: " << prog_name << " <宛先ホスト> [オプション]\n"
              << "\n"
              << "オプション:\n"
              << "  -t min,max   TTL の範囲を min から max に設定\n"
              << "  -t max       TTL の範囲を 1 から max に設定（デフォルト: 7）\n"
              << "  -i interval  送信間隔を秒単位で設定（デフォルト: 1.0）\n"
              << "  -if iface    送信に使うネットワークインタフェースを設定（例: eth0, enp3s0）\n"
              << "\n"
              << "出力形式:\n"
              << "  送信時刻 | hop<TTL>のIPアドレス | ...  （応答なしの場合は *）\n"
              << "  各行は指定間隔ごとに更新され、経路変動をリアルタイムに監視できます。\n"
              << '\n';
}

// ────────────────────────────────────────────
// main
// ────────────────────────────────────────────

int main(int argc, char* argv[]) {
    if (argc < 2) {
        print_usage(argv[0]);
        return 1;
    }

    if (getuid() != 0) {
        std::cerr << "警告: raw ソケットの作成にはroot権限が必要です。\n";
    }

    std::string dest_host;
    int    min_ttl  = 1;
    int    max_ttl  = 7;
    double interval = 1.0;
    std::string iface;

    for (int i = 1; i < argc; ++i) {
        const std::string arg = argv[i];
        if (arg == "-t") {
            if (++i < argc) {
                const std::string range = argv[i];
                const size_t comma = range.find(',');
                if (comma != std::string::npos) {
                    min_ttl = std::stoi(range.substr(0, comma));
                    max_ttl = std::stoi(range.substr(comma + 1));
                } else {
                    max_ttl = std::stoi(range);
                }
            }
        } else if (arg == "-i") {
            if (++i < argc) interval = std::stod(argv[i]);
        } else if (arg == "-if") {
            if (++i < argc) iface = argv[i];
        } else if (arg[0] != '-') {
            dest_host = arg;
        }
    }

    if (dest_host.empty()) {
        std::cerr << "エラー: 宛先ホストが指定されていません。\n";
        print_usage(argv[0]);
        return 1;
    }

    try {
        signal(SIGINT, signal_handler);

        IPv4Address dest_ip;
        try {
            dest_ip = Utils::resolve_domain(dest_host);
        } catch (const std::runtime_error&) {
            std::cerr << "エラー: ホスト名 '" << dest_host << "' を解決できませんでした。\n";
            return 1;
        }

        const auto interval_ms = std::chrono::milliseconds(
            static_cast<long long>(interval * 1000));

        MultiTracer tracer(dest_ip, min_ttl, max_ttl, interval_ms, iface);

        std::cout << "multiroute 開始: " << dest_host
                  << " (" << dest_ip << ")"
                  << "  TTL " << min_ttl << " ～ " << max_ttl << '\n';

        // ヘッダ行（全角文字の表示幅を考慮して pad_center でセンタリング）
        std::cout << pad_center("Time Stamp", TIMESTAMP_WIDTH) << '|';
        for (int i = min_ttl; i <= max_ttl; ++i) {
            std::cout << pad_center("hop " + std::to_string(i), HOP_WIDTH) << '|';
        }
        std::cout << '\n';

        tracer.run();

    } catch (const std::exception& ex) {
        std::cerr << "エラーが発生しました: " << ex.what() << '\n';
        return 1;
    }

    return 0;
}
