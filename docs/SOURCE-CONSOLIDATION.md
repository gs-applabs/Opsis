# OPSIS — consolidated source working tree (R27)

Base: latest unified capability/DeskFlip PR #16, 96bd979a03b736c8d879add9e8af888295196e2f.

Selected non-overlapping changes, with all incoming commit references preserved in SOURCE-CONSOLIDATION.json. Conflicting files are not overwritten. This is a review branch, not a final release. User-side R20 source and original 110-wallpaper installer were not recoverable from Library bytes; preserve them. Existing 100/110 pack and Ancient Athens/Greek scenes must not be overwritten. Ancient Rome final art remains unverified. Keep only one released source ZIP after reconciliation, not earlier generated archives.

## Consolidation summary

| branch | copied | already same | overlaps | proposed deletions retained |
|---|---:|---:|---:|---:|
| appearance | 78 | 0 | 0 | 0 |
| desktop-materials | 5 | 0 | 3 | 0 |
| europe-R26 | 41 | 14 | 17 | 0 |
| europe-R25 | 1 | 0 | 0 | 0 |
| crawler-bridge | 1 | 0 | 4 | 0 |
| main-2.8-followup | 3 | 10 | 24 | 0 |

Legacy add-on handoff: 3 reference files. Its legacy code is documentation only; the active installer source is R26 and the real original add-on installer is missing.

## Available build artifacts (not a release)

- R26 installer: https://github.com/gs-applabs/Opsis/actions/runs/45744541834/artifacts/15082443211
- R26 compatible host: https://github.com/gs-applabs/Opsis/actions/runs/45744541834/artifacts/15082223673
- Photo masters/review layers: R22 15024050313, R23 15023840456, R25 15022663892. Keep newest revision per ID. Do not include rejected synthetic R7/R8/R10 or duplicate raw download batches in new release.

## Outstanding gates

Reconcile every overlapping code file and unrelated-history feature, compare against latest local integrated R20, verify original 110-wallpaper package, complete real-photo mask/geography/rights and remaining London/Podgorica, include Dublin R16 and confirm Ancient Rome. Re-run full Windows build, installer, UI, WebView2, plugin compatibility, security and performance. No final installer until these gates are met. Main is untouched.
