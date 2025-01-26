#include <stdio.h>
#include <signal.h>
#include <time.h>
#include <sys/time.h>
#include <unistd.h>
#include <curses.h>
#include <string.h>
#include <stdlib.h>

#define CHAR_X 8    // 文字の幅
#define CHAR_Y 8    // 文字の高さ
#define SPEACE_X 1  // 文字の間隔
#define SPEACE_Y 1  // 文字の間隔
#define DATE_LEN 27 // 時刻文字列の長さ

// cc tty_clock.c -s -mtune=native -lncurses -lrt && ./a.out
// 時刻表示

void time_print(char *time_string)
{
  static int YX[19][2] = {
      // 文字の位置
      {(CHAR_Y + SPEACE_Y) * 0, (CHAR_X + SPEACE_X) * 0}, //  1 Y
      {(CHAR_Y + SPEACE_Y) * 0, (CHAR_X + SPEACE_X) * 1}, //  2 Y
      {(CHAR_Y + SPEACE_Y) * 0, (CHAR_X + SPEACE_X) * 2}, //  3 Y
      {(CHAR_Y + SPEACE_Y) * 0, (CHAR_X + SPEACE_X) * 3}, //  4 Y
      {(CHAR_Y + SPEACE_Y) * 0, (CHAR_X + SPEACE_X) * 4}, //    -
      {(CHAR_Y + SPEACE_Y) * 0, (CHAR_X + SPEACE_X) * 5}, //  6 M
      {(CHAR_Y + SPEACE_Y) * 0, (CHAR_X + SPEACE_X) * 6}, //  7 M
      {(CHAR_Y + SPEACE_Y) * 0, (CHAR_X + SPEACE_X) * 7}, //    -
      {(CHAR_Y + SPEACE_Y) * 0, (CHAR_X + SPEACE_X) * 8}, //  9 D
      {(CHAR_Y + SPEACE_Y) * 0, (CHAR_X + SPEACE_X) * 9}, // 10 D
      {(CHAR_Y + SPEACE_Y) * 1, (CHAR_X + SPEACE_X) * 0}, // 空白
      {(CHAR_Y + SPEACE_Y) * 1, (CHAR_X + SPEACE_X) * 2}, // 12 H
      {(CHAR_Y + SPEACE_Y) * 1, (CHAR_X + SPEACE_X) * 3}, // 13 H
      {(CHAR_Y + SPEACE_Y) * 1, (CHAR_X + SPEACE_X) * 4}, //    :
      {(CHAR_Y + SPEACE_Y) * 1, (CHAR_X + SPEACE_X) * 5}, // 15 M
      {(CHAR_Y + SPEACE_Y) * 1, (CHAR_X + SPEACE_X) * 6}, // 16 M
      {(CHAR_Y + SPEACE_Y) * 1, (CHAR_X + SPEACE_X) * 7}, //    :
      {(CHAR_Y + SPEACE_Y) * 1, (CHAR_X + SPEACE_X) * 8}, // 18 S
      {(CHAR_Y + SPEACE_Y) * 1, (CHAR_X + SPEACE_X) * 9}, // 19 S
  };
  static char digits[13][CHAR_Y][CHAR_X + 1] = {
      // 13種, 8行, 8文字 + NULL
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

    for (int j = 0; j < CHAR_Y; j++)
    {
      for (int k = 0; k < CHAR_X; k++)
      {
        if (digits[string][j][k] == '*') // '*' を反転スペースに変換
        {
          attron(COLOR_PAIR(1) | A_BOLD | A_REVERSE); // 文字の色を PAIR 1 に設定
          mvprintw(YX[i][0] + j, YX[i][1] + k, " ");
        }
        else
        {
          if (!digits[string][j][k]) // NULL なら以降は表示しない
          {
            break;
          }
          attrset(COLOR_PAIR(2)); // 背景を PAIR 2 に設定
          mvprintw(YX[i][0] + j, YX[i][1] + k, "%c", digits[string][j][k]);
        }
      }
    }
  }
  attrset(COLOR_PAIR(2));
  refresh(); // 画面を更新

  // 表示後の時刻を取得
  struct timeval tv;
  gettimeofday(&tv, NULL);

  int offset = 0;
  offset = strftime(time_string, DATE_LEN, "%Y-%m-%d %T", localtime(&tv.tv_sec));
  snprintf(time_string + offset, DATE_LEN - offset, ".%06ld", tv.tv_usec);

  mvprintw((CHAR_Y + SPEACE_Y) * 2 - SPEACE_Y,
           (CHAR_X + SPEACE_X) * 10 - strlen(time_string) - SPEACE_X - 1,
           "%s", time_string);
  refresh();      // 画面を更新
  curs_set(TRUE); // カーソルの表示
  echo();         // エコーバックon

  return;
}

void handler_term(int sig)
{
  endwin(); // ncurses の終了
  exit;
}

// 時刻表示 & 次の割り込み時刻を計算
void handler_alrm(int sig)
{
  struct timeval tv;
  char time_string[DATE_LEN + 1];

  // 次の割り込みを作成
  static struct sigaction sa;
  static struct itimerspec ts;
  static timer_t timerid;
  static bool initialized = false;

  if (!initialized)
  {
    // 割り込みハンドラを設定
    sa.sa_handler = handler_alrm;
    sa.sa_flags = 0;
    sigemptyset(&sa.sa_mask);
    sigaction(SIGALRM, &sa, NULL); // タイマー割り込みの設定
    sigaction(SIGQUIT, &sa, NULL); // CTRL+\ ですぐに時刻を更新

    sa.sa_handler = handler_term;
    sigaction(SIGTERM, &sa, NULL); // CTRL+C で終了処理

    // POSIXタイマーを作成
    timer_create(CLOCK_REALTIME, NULL, &timerid);
    initialized = true;
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
  strftime(time_string, DATE_LEN, "%Y-%m-%d %T", localtime(&tv.tv_sec));

  // 時刻を表示
  time_print(time_string);

  return;
}

int main()
{
  initscr();                               // cursess の初期化
  start_color();                           // カラー属性の初期化
  init_pair(1, COLOR_GREEN, COLOR_BLACK);  // 文字の色 (前景色のみ有効)
  init_pair(2, COLOR_WHITE, COLOR_YELLOW); // 背景の色 (背景色と、下の時刻の色)

  bkgdset(' ' | COLOR_PAIR(2)); // この属性を画面背景として設定
  clear();                      // 画面全体を背景属性でクリア
  refresh();                    // 変更を反映

  handler_alrm(0); // 割り込みの開始

  bool do_while_exit = false;
  while (!do_while_exit)
  {
    int ch = getch();
    while ((ch = getch()) != ERR)
    {
      if (ch == 'q')
      {
        do_while_exit = true;
        break;
      }
    }

    // 割り込みが発生するまで待機
    pause();
  }

  endwin(); // ncurses の終了
  return 0;
}
