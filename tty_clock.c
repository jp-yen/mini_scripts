#include <stdio.h>
#include <signal.h>
#include <time.h>
#include <sys/time.h>
#include <unistd.h>
#include <curses.h>
// cc term_clock.c -lncurses && ./a.out
// 時刻表示

void time_print(char *time_string){
  static int YX[19][2] = { // 文字の位置
    {  0,  0},	//  1 Y
    {  0,  8},	//  2 Y
    {  0, 16},	//  3 Y
    {  0, 24},	//  4 Y
    {  0, 32},	//    -
    {  0, 40},	//  6 M
    {  0, 48},	//  7 M
    {  0, 56},	//    -
    {  0, 64},	//  8 D
    {  0, 72},	//  9 D
    {  8,  0},	// 空白
    {  8, 16},	// 11 H
    {  8, 24},	// 12 H
    {  8, 32},	//    :
    {  8, 40},	// 14 M
    {  8, 48},	// 15 M
    {  8, 56},	//    :
    {  8, 64},	// 16 S
    {  8, 72},	// 17 S
  };
  static char digits[12][8][9] = { // 12種, 8行, 8文字 + NULL
    {
      " ****** ",
      "*     **",
      "*    * *",
      "*   *  *",
      "*  *   *",
      "* *    *",
      "**     *",
      " ****** "
    },
    {
      "   **   ",
      "  ***   ",
      "   **   ",
      "   **   ",
      "   **   ",
      "   **   ",
      "   **   ",
      " ****** "
    },
    {
      " ****** ",
      "*      *",
      "       *",
      "  ***** ",
      " *      ",
      "*       ",
      "*       ",
      "********"
    },
    {
      " ****** ",
      "*      *",
      "       *",
      " ****** ",
      "       *",
      "       *",
      "*      *",
      " ****** "
    },
    {
      "    **  ",
      "   * *  ",
      "  *  *  ",
      " *   *  ",
      "********",
      "     *  ",
      "     *  ",
      "     *  "
    },
    {
      "********",
      "*       ",
      "*       ",
      "******* ",
      "       *",
      "       *",
      "       *",
      "******* "
    },
    {
      "  ***** ",
      " *      ",
      "*       ",
      "******* ",
      "*      *",
      "*      *",
      "*      *",
      " ****** "
    },
    {
      "********",
      "*      *",
      "*      *",
      "      * ",
      "     *  ",
      "    *   ",
      "    *   ",
      "    *   "
    },
    {
      " ****** ",
      "*      *",
      "*      *",
      " ****** ",
      "*      *",
      "*      *",
      "*      *",
      " ****** "
    },
    {
      " ****** ",
      "*      *",
      "*      *",
      "*      *",
      " ****** ",
      "     *  ",
      "    *   ",
      "   *    "
    },
    { // -
      "",
      "",
      "",
      "",
      " ******",
      "",
      "",
      ""
    },
    { // :
      "",
      "",
      "   **   ",
      "   **   ",
      "",
      "   **   ",
      "   **   ",
      ""
    },
    { // スペース
      "",
      "",
      "",
      "",
      "",
      "",
      "",
      ""
    },
  };
  char string;

  noecho();
  curs_set(FALSE); // カーソルの非表示

	for (int i = 0; i < 19; i++) { // 'YYYY-MM-DD HH:MM:SS' 19文字
		if (time_string[i] == '-') {
			string = 10;
		} else if (time_string[i] == ':') {
			string = 11;
		} else if (time_string[i] == ' ') {
			string = 12;
		} else {
			string = time_string[i] - '0';
		}

		for (int j = 0; j < 8; j++) {
	        mvprintw(YX[i][0] + j, YX[i][1], digits[string][j]);
		}
	}

  mvprintw(16,54, time_string);
	refresh();      // 表示の更新
	curs_set(TRUE); // カーソルの表示
	echo();         // エコーバックon
}

// 時刻表示 & 次の割り込み時刻を計算
void handler(int sig) {
  struct timeval tv;
  char time_string[100];
  long milliseconds;

	// 次の割り込みを作成
  struct sigaction sa;
  struct itimerspec ts;
  timer_t timerid;

  // 割り込みハンドラを設定
  sa.sa_handler = handler;
  sa.sa_flags = 0;
  sigemptyset(&sa.sa_mask);
  sigaction(SIGALRM, &sa, NULL);

  // POSIXタイマーを作成
  timer_create(CLOCK_REALTIME, NULL, &timerid);

  // 現在の時刻を取得
  gettimeofday(&tv, NULL);

  // 次の0秒丁度までの残り時間を計算
  long microseconds_to_wait = 1000000 - tv.tv_usec;
  long seconds_to_wait = microseconds_to_wait / 1000000;
  long nanoseconds_to_wait = (microseconds_to_wait % 1000000) * 1000;

  // 次回のタイマーを設定
  ts.it_value.tv_sec = seconds_to_wait;
  ts.it_value.tv_nsec = nanoseconds_to_wait;
  ts.it_interval.tv_sec = 0;   // 定期的な割り込みはしない
  ts.it_interval.tv_nsec = 0;

	if ( nanoseconds_to_wait < 950000){
    ts.it_value.tv_nsec =  950000;	// 一旦 0.95秒待ってプロセスを起こし、残り時間を待つ
    timer_settime(timerid, 0, &ts, NULL);
		return;
	} else {
    timer_settime(timerid, 0, &ts, NULL);
 	}

  strftime(time_string, sizeof(time_string), "%Y-%m-%d %T", localtime(&tv.tv_sec));

  // 時刻を表示
  milliseconds = tv.tv_usec / 1000;
  sprintf(time_string, "%s.%06ld", time_string, tv.tv_usec);
  time_print(time_string);

  return;
}

int main() {
  initscr();  // cursess の初期化
  handler(0);  // 割り込みの開始

  while (1) {
    int ch = getch();
    if (ch == 'q'){ break; }  // 'q'が入力されたらループを抜ける

    // 割り込みが発生するまで待機
    pause();
  }

  endwin();  // ncurses の終了
  return 0;
}
