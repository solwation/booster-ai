using BoosterAI;
var root = args[0];
Directory.CreateDirectory(root);
Directory.CreateDirectory(root + "/logs");
File.WriteAllText(root + "/tab-lib.sh", TabSession.Lib);
var status = new CliStatus("codex", "function", "/neutral/codex") { Help = "--approve-for-me --dangerously-bypass-approvals-and-sandbox" };
var original = "# This line must never execute in an explicit restart\ntouch '" + root + "/must-not-run'\n";
var auto = AgentModes.Choices(status).Single(c => c.Mode == AgentMode.AutoApprove);
var standard = AgentModes.Choices(status).Single(c => c.Mode == AgentMode.Standard);
File.WriteAllText(root + "/auto.bashrc", AgentModes.Change(original, status, auto, replaceCustomConfirmed: true));
File.WriteAllText(root + "/standard.bashrc", AgentModes.Change(original, status, standard, replaceCustomConfirmed: true));
File.WriteAllText(root + "/run.sh", "#!/bin/bash\nexec /usr/bin/env HOME=" + Launcher.Quote(root) + " /bin/bash --noprofile --norc " + Launcher.Quote(root + "/check.sh") + "\n");
File.WriteAllText(root + "/check.sh", $$"""
#!/bin/bash
cp '{{root}}/auto.bashrc' '{{root}}/.bashrc' || exit 11
codex () {
    printf original
}
alias helper='printf helper'
. '{{root}}/tab-lib.sh' '{{root}}/logs' tab-neutral 'BoosterAI-WT113' || exit 12
bs_refresh_aliases codex || exit 13
[ "$(alias codex)" = "alias codex='codex --approve-for-me'" ] || exit 14
! declare -F codex >/dev/null || exit 15
[ "$(alias helper)" = "alias helper='printf helper'" ] || exit 16
[ ! -f '{{root}}/must-not-run' ] || exit 17
cp '{{root}}/standard.bashrc' '{{root}}/.bashrc' || exit 18
bs_refresh_aliases codex || exit 19
! alias codex >/dev/null 2>&1 || exit 20
! declare -F codex >/dev/null || exit 21
[ ! -f '{{root}}/must-not-run' ] || exit 22
printf '%s\n' booster-wt113-ok > '{{root}}/marker'
exit 0
""" + "\n");
