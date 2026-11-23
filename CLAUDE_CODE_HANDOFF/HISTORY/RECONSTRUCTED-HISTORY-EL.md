# Ανακατασκευασμένο ιστορικό OPSIS

**ΠΡΟΣΟΧΗ:** Αυτό είναι *τεχνητά ανακατασκευασμένο ιστορικό από ZIP checkpoints*. ΔΕΝ είναι ιστορικό commits του αρχικού GitHub, δεν τεκμηριώνει αρχικό συντάκτη/ώρα αλλαγής και δεν πρέπει να συγχωνευθεί ως να ήταν πρωτογενές ιστορικό.

Τα commits δημιουργήθηκαν διαδοχικά από το ΠΛΗΡΕΣ περιεχόμενο των διαθέσιμων ZIP, χωρίς να αλλαχθεί ο κώδικας. Κάθε commit αντιστοιχεί σε αρχείο πηγών, όχι σε μεμονωμένη λειτουργία.

| Snapshot | Αρχεία | Προσθήκες | Τροποποιήσεις | Διαγραφές | Commit | Αρχείο πηγής |
|---|---:|---:|---:|---:|---|---|
| BASE-2402 | 555 | 555 | 0 | 0 | `610bf57c4db4` | `Opsis-Desktop-Customization-2.7.7.2402-net11-performance-source.zip` |
| BASE-2403 | 559 | 4 | 13 | 0 | `ccdee6baad0d` | `Opsis-Desktop-Customization-2.7.7.2403-integrated-source.zip` |
| R2 | 565 | 6 | 9 | 0 | `485c53f294e8` | `Opsis-2.8.0.2500-plugin-dll-1.0.0.0-source-preview-R2.zip` |
| R3 | 574 | 9 | 20 | 0 | `6f87135ef6ec` | `Opsis-Desktop-Customization-2.8.0.2500-100-improvements-R3-source-preview.zip` |
| R4 | 581 | 7 | 18 | 0 | `b54b4e480e2a` | `Opsis-Desktop-Customization-2.8.0.2500-R4-partials-source.zip` |
| R5 | 584 | 3 | 19 | 0 | `5dbfae4d6513` | `Opsis-Desktop-Customization-2.8.0.2500-R5-source-checkpoint.zip` |
| R6 | 585 | 1 | 17 | 0 | `842219a19a1c` | `Opsis-Desktop-Customization-2.8.0.2500-R6-Session-1-of-4-source.zip` |
| R7 | 588 | 3 | 10 | 0 | `e004aec5b859` | `Opsis-Desktop-Customization-2.8.0.2500-R7-Session-2-of-4-source.zip` |
| R8 | 596 | 8 | 14 | 0 | `6e87becbf8c8` | `Opsis-Desktop-Customization-2.8.0.2500-R8-Session-3-of-4-source.zip` |
| R9 | 605 | 9 | 22 | 0 | `5da3ff3a8ea1` | `Opsis-Desktop-Customization-2.8.0.2500-R9-Session-4-of-4-source.zip` |
| R10 | 611 | 6 | 15 | 0 | `7f27f635562b` | `Opsis-Desktop-Customization-2.8.0.2500-R10-partials-source.zip` |

Χρήσιμες εντολές:
```bash
git log --oneline --all
git diff --stat HEAD~1 HEAD
git diff --name-status HEAD~1 HEAD
git show --stat <commit>
```

Για αναλυτική λίστα αρχείων και SHA-256 κάθε ZIP: `HISTORY/reconstructed-revisions.json`. Περιλαμβάνει επίσης τα πραγματικά προστιθέμενα/αλλαγμένα/διαγραμμένα αρχεία ανά checkpoint.

Το τελευταίο commit πριν από τα υλικά παράδοσης είναι το **R10**. Η επιπλέον commit που ακολουθεί εισάγει **μόνο το υλικό παράδοσης στον Claude Code**.
