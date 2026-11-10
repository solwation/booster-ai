# Release 0.36.0 verification

- Tag `v0.36.0`, revision `6019015665eacd618d76954ff8ff76a0b19c1829`; product/test revision `3e3ad586847d0199765d094785cbec04c0160fde`, evidence `0ec4e07434ac36daee73a58f68580c6f04cc2dec`.
- [Release workflow](https://github.com/solwation/booster-ai/actions/runs/42774352109): build, signing and publication succeeded. WSL and Windows each passed 942 tests; both Release builds had zero warnings/errors. See the [feature verification](../../146/verification.md) for the 43 headless checks, neutral screenshots and limits.
- All three published assets were downloaded, and `sha256sum -c SHA256SUMS` passed for the Linux archive, Windows ZIP and Windows setup EXE.
- Native Windows Authenticode checks returned `Valid` for the setup EXE, the ZIP's portable EXE and its installation script. All were signed by Svep Design Center AB with timestamp certificates. Setup and portable EXE SHA256 hashes matched.
- The GitHub `releases/latest` endpoint returned `v0.36.0`, stable and not a draft (published 2026-11-10 07:21:32 UTC).
- Delivery uses automatic updates. No manual Windows installation, actual model download, inference or benchmark was performed. A successful local self-update is not claimed.
- Session review covered `50e07b8..6019015`: source, documentation, release summary, test evidence and all nine neutral UI images. Lifecycle retention and initial minimum-size scrolling were corrected before final verification; no remaining findings. Main-window README screenshots remain representative because the main window did not change.
- After release, only the local default version metadata and this verification record were updated; no untagged functional change was introduced.
