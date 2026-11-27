namespace BoosterAI;

/// <summary>
/// BoosterAI's splash in the middle of a tab before its CLI starts (#242, #244), as tab-lib.sh's bs_logo. The art is pixels drawn
/// with half blocks (two square pixels per character cell): the small icon's mark (assets/boosterai-small.svg, the
/// variant for 16 px: a white bolt on the blue-to-purple tile) beside the wordmark "BoosterAI" in a bold pixel font.
/// </summary>
public static class TabLogo
{
    /// <summary>
    /// The logo script on its own in the session directory: a restart in a shell that sourced an older tab-lib.sh, which
    /// has no bs_logo, loads it from here (#243).
    /// </summary>
    public const string ScriptFile = "tab-logo.sh";

    /// <summary>The mark, 16 × 16 pixels: '#' the gradient tile, '@' the white bolt, '.' nothing.</summary>
    internal static readonly string[] Mark =
    [
        "..############..",
        ".##############.",
        "#########@######",
        "########@@######",
        "#######@@#######",
        "######@@@#######",
        "######@@@@@@####",
        "#####@@@@@@@####",
        "####@@@@@@@#####",
        "#######@@@######",
        "#######@@#######",
        "#######@@#######",
        "#######@########",
        "######@#########",
        ".##############.",
        "..############..",
    ];

    /// <summary>The wordmark's letters, 8 pixels high (rows separated by spaces), stems two pixels wide.</summary>
    private static readonly (char Letter, string Rows)[] Font =
    [
        ('B', "#####. ##..## ##..## #####. ##..## ##..## ##..## #####."),
        ('o', "...... ...... .####. ##..## ##..## ##..## ##..## .####."),
        ('s', "...... ...... .##### ##.... .####. ....## ....## #####."),
        ('t', ".##.. .##.. ##### .##.. .##.. .##.. .##.. ..###"),
        ('e', "...... ...... .####. ##..## ###### ##.... ##.... .#####"),
        ('r', "..... ..... ##.## ###.. ##... ##... ##... ##..."),
        ('A', ".####. ##..## ##..## ##..## ###### ##..## ##..## ##..##"),
        ('I', "## ## ## ## ## ## ## ##"),
    ];

    /// <summary>Columns between the mark and the wordmark.</summary>
    internal const int Gap = 3;

    /// <summary>"BoosterAI" in the pixel font, one pixel between letters.</summary>
    internal static string[] Wordmark()
    {
        var rows = new string[8];
        Array.Fill(rows, "");
        foreach (var (letter, i) in "BoosterAI".Select((c, i) => (c, i)))
        {
            var glyph = Font.Single(f => f.Letter == letter).Rows.Split(' ');
            for (var r = 0; r < 8; r++) rows[r] += (i > 0 ? "." : "") + glyph[r];
        }
        return rows;
    }

    /// <summary>The mark with the wordmark vertically centred beside it.</summary>
    internal static string[] Full()
    {
        var word = Wordmark();
        var top = (Mark.Length - word.Length) / 2;
        return Mark.Select((m, y) => m + new string('.', Gap)
            + (y >= top && y < top + word.Length ? word[y - top] : new string('.', word[0].Length))).ToArray();
    }

    /// <summary>
    /// Pixel rows as character cells, two rows each: one digit per cell, 3 × top + bottom with 0 nothing, 1 gradient and
    /// 2 white (bs_logo_frame draws them).
    /// </summary>
    internal static string[] Cells(string[] pixels)
    {
        static int V(char c) => c switch { '#' => 1, '@' => 2, _ => 0 };
        return Enumerable.Range(0, pixels.Length / 2).Select(y => new string(pixels[2 * y]
            .Select((top, x) => (char)('0' + 3 * V(top) + V(pixels[2 * y + 1][x]))).ToArray())).ToArray();
    }

    /// <summary>Smallest terminal sizes (columns, lines) for each variant; bs_logo_size picks the largest that fits.</summary>
    internal const int FullColumns = 80, FullLines = 16, WordColumns = 60, WordLines = 10, LineColumns = 12, LineLines = 4;

    public static string Script => "\n" + $"bs_logo_full=({string.Join(' ', Cells(Full()))})\n"
        + $"bs_logo_word=({string.Join(' ', Cells(Wordmark()))})\n"
        + $"bs_logo_mark={Mark[0].Length}\n" + """
# bs_logo_depth: 24 (truecolor), 8 (256 colours) or 0 (still and plain: NO_COLOR, or a terminal with neither). Windows
# Terminal (WT_SESSION, passed on to WSL) and VTE/Konsole/most others (COLORTERM) have truecolor.
bs_logo_depth() {
  if [ -n "${NO_COLOR-}" ]; then echo 0
  elif [ "${COLORTERM-}" = truecolor ] || [ "${COLORTERM-}" = 24bit ] || [ -n "${WT_SESSION-}" ] || [[ ${TERM-} == *-direct ]]; then echo 24
  elif [[ ${TERM-} == *256color* ]] || [ "$(tput colors 2>/dev/null || echo 0)" -ge 256 ] 2>/dev/null; then echo 8
  else echo 0
  fi
}

""" + $$"""
# bs_logo_size <columns> <lines>: the largest variant that fits: full (mark and wordmark), word, line ("BoosterAI") or none.
bs_logo_size() {
  if [ "$1" -ge {{FullColumns}} ] && [ "$2" -ge {{FullLines}} ]; then echo full
  elif [ "$1" -ge {{WordColumns}} ] && [ "$2" -ge {{WordLines}} ]; then echo word
  elif [ "$1" -ge {{LineColumns}} ] && [ "$2" -ge {{LineLines}} ]; then echo line
  else echo none
  fi
}

""" + """
# bs_logo_rgb <x> <width> <glow 0..100> [fade 0..100]: the icon's gradient (rgb 30,102,245 to 136,57,239) at x of width,
# lightened by glow and faded towards a dark background (rgb 12,12,12) by fade, as an SGR colour (2;r;g;b, or 5;n from
# the 256-colour cube) in bs_logo_c.
bs_logo_rgb() {
  local d=$(($2 > 1 ? $2 - 1 : 1)) r g b k=${4:-0}
  r=$((30 + 106 * $1 / d)) g=$((102 - 45 * $1 / d)) b=$((245 - 6 * $1 / d))
  r=$((r + (255 - r) * $3 / 100)) g=$((g + (255 - g) * $3 / 100)) b=$((b + (255 - b) * $3 / 100))
  r=$((r - (r - 12) * k / 100)) g=$((g - (g - 12) * k / 100)) b=$((b - (b - 12) * k / 100))
  if [ "$bs_logo_d" = 24 ]; then bs_logo_c="2;$r;$g;$b"
  else bs_logo_c="5;$((16 + 36 * ((r * 5 + 127) / 255) + 6 * ((g * 5 + 127) / 255) + (b * 5 + 127) / 255))"
  fi
}
# bs_logo_frame <edge> <band> [fade]: one frame of bs_logo_art (cells: 3 * top + bottom pixel, 0 nothing, 1 gradient,
# 2 white) into bs_logo_out, each row placed at bs_logo_top + row, column bs_logo_left. The first bs_logo_m columns are
# the mark, with the icon's diagonal gradient, the rest the wordmark, with the gradient from left to right. Columns
# right of the edge are still blank; those within band of it glow; fade darkens everything (the splash fading out).
bs_logo_frame() {
  local w=${#bs_logo_art[0]} n=${#bs_logo_art[@]} m=$bs_logo_m k=${3:-0} x y glow last s g e=$'\e' z=$'\e[0m' wf wb line row
  local -a f b
  bs_logo_out=
  if [ "$bs_logo_d" = 24 ]; then
    x=$((255 - 243 * k / 100)); wf="$e[38;2;$x;$x;${x}m" wb="$e[48;2;$x;$x;${x}m"
  else
    x=$((k > 0 ? 255 - 23 * k / 100 : 231)); wf="$e[38;5;${x}m" wb="$e[48;5;${x}m"
  fi
  for ((y = 0; y < n; y++)); do
    row=${bs_logo_art[y]} line="$e[$((bs_logo_top + y));${bs_logo_left}H" last=
    for ((x = 0; x <= $1 && x < ${bs_logo_end[y]}; x++)); do
      glow=$(($1 - x))
      if [ "$glow" -ge "$2" ]; then glow=0; else glow=$((80 * ($2 - glow) / $2)); fi
      if [ "$x" -lt "$m" ]; then bs_logo_rgb $((x + 2 * y)) $((m + 2 * n - 2)) "$glow" "$k"; f[x]="$e[38;${bs_logo_c}m" b[x]="$e[48;${bs_logo_c}m"
      elif [ -z "${f[x]}" ]; then bs_logo_rgb $((x - m)) $((w - m)) "$glow" "$k"; f[x]="$e[38;${bs_logo_c}m" b[x]="$e[48;${bs_logo_c}m"
      fi
      case ${row:x:1} in
        0) s=$z g=' ' ;;
        1) s=$z${f[x]} g='▄' ;;
        2) s=$z$wf g='▄' ;;
        3) s=$z${f[x]} g='▀' ;;
        4) s=$z${f[x]} g='█' ;;
        5) s=${f[x]}$wb g='▀' ;;
        6) s=$z$wf g='▀' ;;
        7) s=$wf${b[x]} g='▀' ;;
        8) s=$z$wf g='█' ;;
      esac
      [ "$s" = "$last" ] || line+=$s last=$s
      line+=$g
    done
    bs_logo_out+="$line$z"
  done
}
# bs_logo_plain: bs_logo_art without colour, centred with blank lines and spaces; the white bolt stays a gap in the mark.
bs_logo_plain() {
  local y x row line
  for ((y = 1; y < bs_logo_top; y++)); do echo; done
  for ((y = 0; y < ${#bs_logo_art[@]}; y++)); do
    row=${bs_logo_art[y]} line=$(printf '%*s' $((bs_logo_left - 1)) '')
    for ((x = 0; x < ${bs_logo_end[y]}; x++)); do
      case ${row:x:1} in
        1|7) line+='▄' ;;
        3|5) line+='▀' ;;
        4) line+='█' ;;
        *) line+=' ' ;;
      esac
    done
    printf '%s\n' "$line"
  done
}
# bs_logo: BoosterAI's splash in a tab, after bs_reset and before its CLI or its update starts (#242, #244): the icon's
# mark and the wordmark in the middle of the tab on the terminal's alternate screen, revealed by a gradient sweep (about
# half a second), held briefly and faded out; then the alternate screen is left and the CLI starts on the clean screen,
# with nothing of the splash in the scrollback. Frames are dropped past their deadlines (0.8 s for the sweep, 1.5 s in
# all, setup included) on a slow machine. Narrower tabs get the wordmark or one line, very narrow ones nothing; NO_COLOR
# or a terminal without 256 colours gets it still and plain for a moment; nothing without a terminal. The cursor is
# hidden meanwhile and the screen and cursor restored however it ends; keys typed meanwhile reach the CLI. Runs in a
# subshell: nothing stays behind.
bs_logo() (
  [ -t 1 ] || exit 0
  case ${TERM-} in ''|dumb) exit 0 ;; esac
  local size cols lines e=$'\e' i k n w band t t0=${EPOCHREALTIME/[.,]/} text=BoosterAI out
  read -r lines cols < <(stty size 2>/dev/null </dev/tty)
  [ "${cols:-0}" -gt 0 ] 2>/dev/null || cols=${COLUMNS:-$(tput cols 2>/dev/null || echo 80)}
  [ "${lines:-0}" -gt 0 ] 2>/dev/null || lines=${LINES:-$(tput lines 2>/dev/null || echo 24)}
  size=$(bs_logo_size "$cols" "$lines")
  bs_logo_d=$(bs_logo_depth)
  bs_logo_past() { [ -n "$t0" ] && [ $((${EPOCHREALTIME/[.,]/} - t0)) -ge "$1" ]; }
  case $size in
    none) exit 0 ;;
    line) bs_logo_art=() ;;
    full) bs_logo_art=("${bs_logo_full[@]}") bs_logo_m=$bs_logo_mark ;;
    *) bs_logo_art=("${bs_logo_word[@]}") bs_logo_m=0 ;;
  esac
  if [ "$size" = line ]; then w=${#text} n=1; else w=${#bs_logo_art[0]} n=${#bs_logo_art[@]}; fi
  bs_logo_top=$(((lines - n) / 2 + 1)) bs_logo_left=$(((cols - w) / 2 + 1)) bs_logo_end=()
  for ((i = 0; i < ${#bs_logo_art[@]}; i++)); do t=${bs_logo_art[i]%"${bs_logo_art[i]##*[1-8]}"}; bs_logo_end+=("${#t}"); done
  if [ "$bs_logo_d" = 0 ]; then
    # Still and plain; tput clears the screen where the terminal has a way to (on its alternate screen if it has one).
    trap 'tput clear 2>/dev/null' EXIT
    tput smcup 2>/dev/null; tput clear 2>/dev/null
    if [ "$size" = line ]; then bs_logo_art=() bs_logo_plain; printf '%*s%s\n' $((bs_logo_left - 1)) '' "$text"; else bs_logo_plain; fi
    sleep 0.6
    tput clear 2>/dev/null; tput rmcup 2>/dev/null
    trap - EXIT
    exit 0
  fi
  trap 'printf "\033[0m\033[2J\033[?1049l\033[?25h"' EXIT
  trap 'exit 130' INT TERM HUP
  printf '\033[?1049h\033[?25l\033[2J'
  if [ "$size" = line ]; then
    for k in 0 0 0 0 0 0 0 0 0 0 20 40 60 80; do
      bs_logo_past 1500000 && break
      out="$e[${bs_logo_top};${bs_logo_left}H"
      for ((i = 0; i < w; i++)); do bs_logo_rgb "$i" "$w" 0 "$k"; out+="$e[1;38;${bs_logo_c}m${text:i:1}"; done
      printf '%s\033[0m' "$out"
      sleep 0.06
    done
    exit 0
  fi
  band=$((w / 4))
  for ((i = 0; i <= w + band; i += (w + band) / 12 + 1)); do
    bs_logo_past 800000 && break
    bs_logo_frame "$i" "$band"
    printf '%s' "$bs_logo_out"
    sleep 0.035
  done
  bs_logo_frame "$w" 0
  printf '%s' "$bs_logo_out"
  bs_logo_past 1100000 || sleep 0.3
  for k in 20 40 60 80 100; do
    bs_logo_past 1500000 && break
    bs_logo_frame "$w" 0 "$k"
    printf '%s' "$bs_logo_out"
    sleep 0.04
  done
)

""";
}
