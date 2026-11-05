namespace BoosterAI;

/// <summary>Refresh only aliases whose ownership was established when the tab's library was sourced.</summary>
internal static class AgentAliasRuntime
{
    public static string Script => "\n" + """
# Permission aliases are refreshed only at an explicit CLI start/restart, never mid-turn.
# No text from .bashrc is evaluated: only the exact reviewed cases below become alias values.
declare -A bs_owned_aliases
bs_alias_known() {
  bs_alias_line= bs_alias_value=
  case "$1:$2" in
""" + Cases() + """
    *) return 1 ;;
  esac
  bs_alias_line="$2"
}
bs_override_read() {
  local cli="$1" line active=0 stage=0 starts=0 ends=0
  bs_alias_line= bs_alias_value=
  [ -f ~/.bashrc ] || return 2
  while IFS= read -r line || [ -n "$line" ]
  do
    line=${line%$'\r'}
    if [ "$line" = "# >>> BoosterAI permissions for $cli >>>" ]; then
      starts=$((starts + 1)); active=1; continue
    fi
    if [ "$line" = "# <<< BoosterAI permissions for $cli <<<" ]; then
      [ "$active" = 1 ] || return 1
      ends=$((ends + 1)); active=0; continue
    fi
    [ "$active" = 1 ] || continue
    case "$line" in ''|\#*) continue ;; esac
    case "$stage:$line" in
      "0:unalias $cli 2>/dev/null || true") stage=1 ;;
      "1:unset -f $cli 2>/dev/null || true") stage=2 ;;
      *) [ "$stage" = 2 ] && bs_alias_known "$cli" "$line" || return 1; stage=3 ;;
    esac
  done < ~/.bashrc
  [ "$starts" != 0 ] || { [ "$ends" = 0 ] && return 2; return 1; }
  [ "$starts" = 1 ] && [ "$ends" = 1 ] && [ "$active" = 0 ] && [ "$stage" -ge 2 ]
}
bs_alias_read() {
  local override_result=0
  bs_override_read "$1" || override_result=$?
  [ "$override_result" = 2 ] || return "$override_result"
  local cli="$1" line in_block=0 starts=0 ends=0 found=0
  bs_alias_line= bs_alias_value=
  [ -f ~/.bashrc ] || return 0
  while IFS= read -r line || [ -n "$line" ]
  do
    line=${line%$'\r'}
    if [ "$line" = '# >>> BoosterAI full-auto aliases >>>' ]; then
      starts=$((starts + 1)); in_block=1; continue
    fi
    if [ "$line" = '# <<< BoosterAI full-auto aliases <<<' ]; then
      [ "$in_block" = 1 ] || return 1
      ends=$((ends + 1)); in_block=0; continue
    fi
    case "$line" in *'# >>> BoosterAI full-auto aliases >>>'*|*'# <<< BoosterAI full-auto aliases <<<'*) return 1 ;; esac
    if [[ $line =~ ^[[:space:]]*(alias[[:space:]]+([^[:space:]]+[[:space:]]+)*$cli=|function[[:space:]]+$cli([[:space:]]|\()|$cli[[:space:]]*\() ]]; then
      [ "$in_block" = 1 ] && [ "$found" = 0 ] || return 1
      bs_alias_known "$cli" "$line" || return 1
      found=1
    fi
  done < ~/.bashrc
  [ "$starts" -le 1 ] && [ "$starts" = "$ends" ] && [ "$in_block" = 0 ]
}
bs_alias_capture() {
  local cli actual
  for cli in claude codex agy aider interpreter openhands
  do
    declare -F "$cli" >/dev/null && continue
    actual=$(alias "$cli" 2>/dev/null) || actual=
    if bs_alias_read "$cli" && [ "$actual" = "$bs_alias_line" ]; then bs_owned_aliases[$cli]="$actual"; fi
  done
}
bs_refresh_aliases() {
  local cli actual
  for cli in "$@"
  do
    case "$cli" in claude|codex|agy|aider|interpreter|openhands) ;; *) continue ;; esac
    # A canonical override records explicit consent to replace this name, including a function.
    local override_result=0
    bs_override_read "$cli" || override_result=$?
    if [ "$override_result" = 0 ]; then
      unalias "$cli" 2>/dev/null || true
      unset -f "$cli" 2>/dev/null || true
      [ -z "$bs_alias_value" ] || alias "$cli=$bs_alias_value"
      bs_owned_aliases[$cli]="$bs_alias_line"
      continue
    fi
    [ "$override_result" = 2 ] || continue
    [ -n "${bs_owned_aliases[$cli]+known}" ] || continue
    declare -F "$cli" >/dev/null && continue
    actual=$(alias "$cli" 2>/dev/null) || actual=
    [ "$actual" = "${bs_owned_aliases[$cli]}" ] || continue
    if bs_alias_read "$cli"; then
      unalias "$cli" 2>/dev/null || true
      [ -z "$bs_alias_value" ] || alias "$cli=$bs_alias_value"
      bs_owned_aliases[$cli]="$bs_alias_line"
    fi
  done
}
bs_alias_capture

""";

    private static string Cases()
    {
        var result = "\n";
        foreach (var cli in Prerequisites.Clis)
            foreach (var choice in AgentModes.AllKnownChoices(cli).Where(c => c.Mode is AgentMode.AutoApprove or AgentMode.Yolo))
            {
                var line = AgentModes.AliasLine(cli, choice);
                result += "    " + Launcher.Quote(cli + ":" + line) + ") bs_alias_value=" + Launcher.Quote(cli + " " + choice.Arguments) + " ;;\n";
            }
        return result;
    }
}
