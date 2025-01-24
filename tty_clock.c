#include <stdio.h>
#include <signal.h>
#include <time.h>
#include <sys/time.h>
#include <unistd.h>
#include <curses.h>

#define CHAR_X 9
#define CHAR_Y 9

// cc tty_clock.c -s -lncurses -lrt && ./a.out
// 時刻表示

void time_print(char *time_string)
{
  static int YX[19][2] = {
      // 文字の位置
      {CHAR_Y * 0, CHAR_X * 0}, //  1 Y
      {CHAR_Y * 0, CHAR_X * 1}, //  2 Y
      {CHAR_Y * 0, CHAR_X * 2}, //  3 Y
      {CHAR_Y * 0, CHAR_X * 3}, //  4 Y
      {CHAR_Y * 0, CHAR_X * 4}, //    -
      {CHAR_Y * 0, CHAR_X * 5}, //  6 M
      {CHAR_Y * 0, CHAR_X * 6}, //  7 M
      {CHAR_Y * 0, CHAR_X * 7}, //    -
      {CHAR_Y * 0, CHAR_X * 8}, //  9 D
      {CHAR_Y * 0, CHAR_X * 9}, // 10 D
      {CHAR_Y * 1, CHAR_X * 0}, // 空白
      {CHAR_Y * 1, CHAR_X * 2}, // 12 H
      {CHAR_Y * 1, CHAR_X * 3}, // 13 H
      {CHAR_Y * 1, CHAR_X * 4}, //    :
      {CHAR_Y * 1, CHAR_X * 5}, // 15 M
      {CHAR_Y * 1, CHAR_X * 6}, // 16 M
      {CHAR_Y * 1, CHAR_X * 7}, //    :
      {CHAR_Y * 1, CHAR_X * 8}, // 18 S
      {CHAR_Y * 1, CHAR_X * 9}, // 19 S
  };
  static char digits[13][8][9] = {
      // 12種, 8行, 8文字 + NULL
      {// 0
       " ****** ",
       "*     **",
       "*    * *",
       "*   *  *",
       "*  *   *",
       "* *    *",
       "**     *",
       " ****** "},
      {// 1
       "   **   ",
       "  ***   ",
       "   **   ",
       "   **   ",
       "   **   ",
       "   **   ",
       "   **   ",
       " ****** "},
      {// 2
       " ****** ",
       "*      *",
       "       *",
       "  ***** ",
       " *      ",
       "*       ",
       "*       ",
       "********"},
      {// 3
       " ****** ",
       "*      *",
       "       *",
       " ****** ",
       "       *",
       "       *",
       "*      *",
       " ****** "},
      {// 4
       "    **  ",
       "   * *  ",
       "  *  *  ",
       " *   *  ",
       "********",
       "     *  ",
       "     *  ",
       "     *  "},
      {// 5
       "********",
       "*       ",
       "*       ",
       "******* ",
       "       *",
       "       *",
       "       *",
       "******* "},
      {// 6
       "  ***** ",
       " *      ",
       "*       ",
       "******* ",
       "*      *",
       "*      *",
       "*      *",
       " ****** "},
      {// 7
       "********",
       "*      *",
       "*      *",
       "      * ",
       "     *  ",
       "    *   ",
       "    *   ",
       "    *   "},
      {// 8
       " ****** ",
       "*      *",
       "*      *",
       " ****** ",
       "*      *",
       "*      *",
       "*      *",
       " ****** "},
      {// 9
       " ****** ",
       "*      *",
       "*      *",
       "*      *",
       " ****** ",
       "     *  ",
       "    *   ",
       "   *    "},
      {// -
       "",
       "",
       "",
       "",
       " ******",
       "",
       "",
       ""},
      {// :
       "",
       "",
       "   **",
       "   **",
       "",
       "   **",
       "   **",
       ""},
      {// スペース
       "",
       "",
       "",
       "",
       "",
       "",
       "",
       ""},
  };
  char string;

  noecho();
  curs_set(FALSE); // カーソルの非表示

  for (int i = 0; i < 19; i++)
  { // 'YYYY-MM-DD HH:MM:SS' 19文字
    switch (time_string[i])
    {
    case '-':
      string = 10;
      break;
    case ':':
      string = 11;
      break;
    case ' ':
      string = 12;
      break;
    default:
      string = time_string[i] - '0';
    }

    // ...existing code...
    for (int j = 0; j < 8; j++)
    {
      for (int k = 0; k < 8; k++)
      {
        if (digits[string][j][k] == '*') // '*' を反転スペースに変換
        {
          attron(COLOR_PAIR(1) | A_REVERSE);
          mvprintw(YX[i][0] + j, YX[i][1] + k, " ");
          attroff(COLOR_PAIR(1) | A_REVERSE);
        }
        else
        {
          if (!digits[string][j][k])
          {
            break;
          }
          mvprintw(YX[i][0] + j, YX[i][1] + k, "%c", digits[string][j][k]);
        }
      }
    }
  }

  mvprintw(CHAR_Y * 2, CHAR_X * 10 - 28, "%s", time_string);
  refresh();      // 表示の更新
  curs_set(TRUE); // カーソルの表示
  echo();         // エコーバックon
}

// 時刻表示 & 次の割り込み時刻を計算
void handler(int sig)
{
  struct timeval tv;
  char time_string[28];

  // 次の割り込みを作成
  static struct sigaction sa;
  static struct itimerspec ts;
  static timer_t timerid;
  static int initialized = 0;

  if (!initialized)
  {
    // 割り込みハンドラを設定
    sa.sa_handler = handler;
    sa.sa_flags = 0;
    sigemptyset(&sa.sa_mask);
    sigaction(SIGALRM, &sa, NULL);

    // POSIXタイマーを作成
    timer_create(CLOCK_REALTIME, NULL, &timerid);
    initialized = 1;
  }

  // 現在の時刻を取得
  gettimeofday(&tv, NULL);

  // 次の0秒丁度までの残り時間を計算
  long microseconds_to_wait = 1000000 - tv.tv_usec;
  long seconds_to_wait = microseconds_to_wait / 1000000;
  long nanoseconds_to_wait = (microseconds_to_wait % 1000000) * 1000;

  // 次回のタイマーを設定
  ts.it_value.tv_sec = seconds_to_wait;
  ts.it_value.tv_nsec = nanoseconds_to_wait;
  ts.it_interval.tv_sec = 0; // 定期的な割り込みはしない
  ts.it_interval.tv_nsec = 0;
  timer_settime(timerid, 0, &ts, NULL);

  // 文字列を生成、表示ルーチンへ
  snprintf(time_string, sizeof(time_string),
           "%04d-%02d-%02d %02d:%02d:%02d.%06ld",
           localtime(&tv.tv_sec)->tm_year + 1900,
           localtime(&tv.tv_sec)->tm_mon + 1,
           localtime(&tv.tv_sec)->tm_mday,
           localtime(&tv.tv_sec)->tm_hour,
           localtime(&tv.tv_sec)->tm_min,
           localtime(&tv.tv_sec)->tm_sec,
           tv.tv_usec);
  time_print(time_string);

  return;
}

int main()
{
  initscr();                              // cursess の初期化
  start_color();                          // カラー属性の初期化
  init_pair(1, COLOR_WHITE, COLOR_BLACK); // カスタムカラーの設定

  handler(0); // 割り込みの開始

  while (1)
  {
    int ch = getch();
    if (ch == 'q')
    {
      break;
    } // 'q'が入力されたらループを抜ける

    // 割り込みが発生するまで待機
    pause();
  }

  endwin(); // ncurses の終了
  return 0;
}
