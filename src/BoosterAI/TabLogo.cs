namespace BoosterAI;

/// <summary>
/// BoosterAI's logo at the top of a tab before its CLI starts (#242), as tab-lib.sh's bs_logo. The art is pixels drawn
/// with half blocks (two square pixels per character cell): the small icon's mark (assets/boosterai-small.svg, the
/// variant for 16 px: a white bolt on the blue-to-purple tile) beside the wordmark "BoosterAI" in a bold pixel font.
/// </summary>
public static class TabLogo
{
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
# bs_logo_rgb <x> <width> <glow 0..100>: the icon's gradient (rgb 30,102,245 to 136,57,239) at x of width, lightened by glow, as an
# SGR colour (2;r;g;b, or 5;n from the 256-colour cube) in bs_logo_c.
bs_logo_rgb() {
  local d=$(($2 > 1 ? $2 - 1 : 1)) r g b
  r=$((30 + 106 * $1 / d)) g=$((102 - 45 * $1 / d)) b=$((245 - 6 * $1 / d))
  r=$((r + (255 - r) * $3 / 100)) g=$((g + (255 - g) * $3 / 100)) b=$((b + (255 - b) * $3 / 100))
  if [ "$bs_logo_d" = 24 ]; then bs_logo_c="2;$r;$g;$b"
  else bs_logo_c="5;$((16 + 36 * ((r * 5 + 127) / 255) + 6 * ((g * 5 + 127) / 255) + (b * 5 + 127) / 255))"
  fi
}
# bs_logo_frame <edge> <band>: one frame of bs_logo_art (cells: 3 * top + bottom pixel, 0 nothing, 1 gradient, 2 white)
# into bs_logo_out. The first bs_logo_m columns are the mark, with the icon's diagonal gradient, the rest the wordmark,
# with the gradient from left to right. Columns right of the edge are still blank; those within band of it glow.
bs_logo_frame() {
  local w=${#bs_logo_art[0]} n=${#bs_logo_art[@]} m=$bs_logo_m x y glow last s g e=$'\e' z=$'\e[0m' wf wb line row
  local -a f b
  bs_logo_out=
  if [ "$bs_logo_d" = 24 ]; then wf="$e[38;2;255;255;255m" wb="$e[48;2;255;255;255m"; else wf="$e[38;5;231m" wb="$e[48;5;231m"; fi
  for ((y = 0; y < n; y++)); do
    row=${bs_logo_art[y]} line=$bs_logo_pad last=
    for ((x = 0; x <= $1 && x < ${bs_logo_end[y]}; x++)); do
      glow=$(($1 - x))
      if [ "$glow" -ge "$2" ]; then glow=0; else glow=$((80 * ($2 - glow) / $2)); fi
      if [ "$x" -lt "$m" ]; then bs_logo_rgb $((x + 2 * y)) $((m + 2 * n - 2)) "$glow"; f[x]="$e[38;${bs_logo_c}m" b[x]="$e[48;${bs_logo_c}m"
      elif [ -z "${f[x]}" ]; then bs_logo_rgb $((x - m)) $((w - m)) "$glow"; f[x]="$e[38;${bs_logo_c}m" b[x]="$e[48;${bs_logo_c}m"
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
    bs_logo_out+="$line$z"$'\e[K\n'
  done
}
# bs_logo_plain: bs_logo_art without colour; the white bolt stays a gap in the mark.
bs_logo_plain() {
  local y x row line
  for ((y = 0; y < ${#bs_logo_art[@]}; y++)); do
    row=${bs_logo_art[y]} line=$bs_logo_pad
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
# bs_logo: BoosterAI's logo at the top of a tab, after bs_reset and before its CLI or its update starts (#242): the icon's
# mark and the wordmark, revealed by a gradient sweep that takes about half a second (frames are dropped past 0.8 s, on a
# slow machine). Narrower tabs get the wordmark or one line, very narrow ones nothing; NO_COLOR or a terminal without 256
# colours gets it still and plain; nothing without a terminal. The CLI starts below it. The cursor is hidden while it
# moves and shown again however it ends; keys typed meanwhile reach the CLI. Runs in a subshell: nothing stays behind.
bs_logo() (
  [ -t 1 ] || exit 0
  case ${TERM-} in ''|dumb) exit 0 ;; esac
  local size cols lines e=$'\e' i n w band t t0 text=BoosterAI out=' '
  read -r lines cols < <(stty size 2>/dev/null </dev/tty)
  [ "${cols:-0}" -gt 0 ] 2>/dev/null || cols=${COLUMNS:-$(tput cols 2>/dev/null || echo 80)}
  [ "${lines:-0}" -gt 0 ] 2>/dev/null || lines=${LINES:-$(tput lines 2>/dev/null || echo 24)}
  size=$(bs_logo_size "$cols" "$lines")
  bs_logo_d=$(bs_logo_depth)
  case $size in
    none) exit 0 ;;
    line)
      if [ "$bs_logo_d" = 0 ]; then printf ' %s\n\n' "$text"; exit 0; fi
      for ((i = 0; i < ${#text}; i++)); do bs_logo_rgb "$i" "${#text}" 0; out+="$e[1;38;${bs_logo_c}m${text:i:1}"; done
      printf '%s\033[0m\n\n' "$out"
      exit 0 ;;
    full) bs_logo_art=("${bs_logo_full[@]}") bs_logo_m=$bs_logo_mark ;;
    *) bs_logo_art=("${bs_logo_word[@]}") bs_logo_m=0 ;;
  esac
  w=${#bs_logo_art[0]} n=${#bs_logo_art[@]}
  bs_logo_pad='  ' bs_logo_end=()
  for ((i = 0; i < n; i++)); do t=${bs_logo_art[i]%"${bs_logo_art[i]##*[1-8]}"}; bs_logo_end+=("${#t}"); done
  if [ "$bs_logo_d" = 0 ]; then bs_logo_plain; echo; exit 0; fi
  trap 'printf "\033[0m\033[?25h"' EXIT
  trap 'exit 130' INT TERM HUP
  printf '\033[?25l'
  band=$((w / 4)) t0=${EPOCHREALTIME/[.,]/}
  for ((i = 0; i <= w + band; i += (w + band) / 12 + 1)); do
    [ -z "$t0" ] || [ $((${EPOCHREALTIME/[.,]/} - t0)) -lt 800000 ] || break
    bs_logo_frame "$i" "$band"
    printf '%s\033[%dA' "$bs_logo_out" "$n"
    sleep 0.035
  done
  bs_logo_frame "$w" 0
  printf '%s\n' "$bs_logo_out"
)

""";
}
