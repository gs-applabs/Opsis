# OPSIS 2.8.0.2500 — Οδηγός παράδοσης προς Claude Code

## Τι περιλαμβάνει το πακέτο
- **Ολόκληρος ο πηγαίος κώδικας R10** από το παρεχόμενο ZIP, χωρίς τροποποιήσεις από αυτή τη διαδικασία παράδοσης, μαζί με assets ελληνικών τοπίων, JS/HTML, μεταφράσεις, βιβλιοθήκες `lib`, installer scripts και υπάρχοντα QA artifacts.
- **Ανακατασκευασμένο τοπικό Git ιστορικό** 11 διαδοχικών snapshots: `BASE-2402`, `BASE-2403`, `R2`, `R3`, `R4`, `R5`, `R6`, `R7`, `R8`, `R9`, `R10`. Δεν είναι πρωτογενές Git history, δεν αντικαθιστά το GitHub.
- `CLAUDE.md`: οδηγίες που διαβάζει αυτόματα το Claude Code.
- `CLAUDE_CODE_HANDOFF/TASKS-AND-ACCEPTANCE-EL.md`: συγκεκριμένες εργασίες, κριτήρια και προτεραιότητες.
- `CLAUDE_CODE_HANDOFF/QA-WINDOWS-RUNBOOK-EL.md`: ακριβής ακολουθία build/tests/installer/compatibility.
- `CLAUDE_CODE_HANDOFF/HISTORY/reconstructed-revisions.json`: αρχεία, SHA-256 αρχικών ZIP, αλλαγές ανά στάδιο.
- `CLAUDE_CODE_HANDOFF/STATUS-100.csv`: όλα τα 100 σημεία σε δομημένη μορφή.
- `CLAUDE_CODE_HANDOFF/SOURCE-INVENTORY-SHA256.csv`: κατάλογος και hash κάθε αρχικού αρχείου R10.
- Αρχικές εκδοτικές/τεχνικές αναφορές στο root (`ROADMAP-100-EL.md`, `R4-...`, `R5-...`, `R6-...` έως `R10-...`, `QA/*`).

## Πραγματική κατάσταση
**R10 = checkpoint πηγαίου κώδικα / όχι πιστοποιημένη έκδοση.** Στην τελευταία καταγραφή: 73 νέες υλοποιήσεις στον source, 17 παλαιότερες, 7 μερικά σημεία (#12, #17, #20, #24, #32, #40, #97) και 3 υποχρεωτικές Windows επικυρώσεις (#92, #93, #99). Αυτοί οι αριθμοί προέρχονται από το source tracker και **δεν αποτελούν ανεξάρτητη επιβεβαίωση**.

Το αρχείο `QA/node-test-r10.txt` καταγράφει 39/39 Node tests επιτυχή σε προηγούμενη εκτέλεση. Δεν υπάρχει αποδεικτικό επιτυχούς `dotnet build/publish` της ίδιας βάσης, Windows UI E2E ή τελικού Inno Setup 7 installer. Στο προηγούμενο Linux περιβάλλον τα `dotnet` και `ISCC.exe` έλειπαν. **Μην αναγάγετε αυτά τα αποτελέσματα σε Windows certification.**

## Πώς να ξεκινήσει ο Claude Code
1. Αποσυμπίεσε όλο το ZIP σε φάκελο με σύντομη διαδρομή, π.χ. `C:\src\Opsis280Review` (με εμφανή τα κρυφά `.git`). Άνοιξε αυτόν τον φάκελο με Claude Code.
2. Δώσε στον Claude το κείμενο του `PROMPT-FOR-CLAUDE-CODE-EL.md`. Το `CLAUDE.md` και οι υπόλοιπες αναφορές θα είναι ήδη διαθέσιμα.
3. Εκτέλεσε `git log --oneline`, `git status`, `git diff` για ανασκόπηση. **Μη θεωρείς τους reconstructed commits επίσημους**.
4. Ξεκίνα με πραγματικό Windows compile/restore και triage πιθανών compile errors. Ολοκλήρωσε τις 7 μερικές υλοποιήσεις και τις 3 επιβεβαιώσεις.
5. Έλεγξε τα plugins από τα πραγματικά GitHub repos, όχι μόνο τα προπακεταρισμένα DLL.
6. Πριν από οποιοδήποτε release, τρέξε εγκατάσταση/αναβάθμιση/απεγκατάσταση σε καθαρό Windows 11 VM, ελέγχους ασφαλείας και πολυοθονικών καταστάσεων, και υπέβαλε τεκμηριωμένο αποτέλεσμα.

## Προσοχή στα εξαρτώμενα έργα
Crawler: `gs-applabs/OpsisNewsCrawler`; Χρονικολόγιο: `gs-applabs/san-shmera`; Opsis.UI: ιδιωτικό `gs-applabs/Opsis.UI`; Media Suite: `gs-applabs/OpsisMediaSuite`; Appearance: `gs-applabs/OpsisAppearance`. Χρειάζεται πρόσβαση GitHub για ολοκληρωμένες δοκιμές σε καθένα τους.

## Ακρίβεια ιστορικού
Το τοπικό Git αναδημιουργήθηκε με ανάγνωση 11 πλήρων ZIP checkpoints. Δεν υπάρχει απόδειξη αρχικών commit IDs, αρχικών author timestamps ή ότι κάποια εργασία πέρασε τεστ κατά τον χρόνο υλοποίησης. Δεν έχει γίνει αναβάθμιση του επίσημου αποθετηρίου από την παρούσα συσκευασία.
