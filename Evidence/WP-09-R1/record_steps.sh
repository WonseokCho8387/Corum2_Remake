#!/usr/bin/env bash
# WP-09-R1 recorded runs on the clean R1 commit: main, plane, dev, slide (video).
S=/c/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad
P=/d/ProjectR/Corum2Remake
cd $P; git status --short; git log --oneline -1
rm -rf $P/Diagnostics/WP09/r1main $P/Diagnostics/WP09/r1plane $P/Diagnostics/WP09/r1dev $P/Diagnostics/WP09/r1slide
for k in main plane dev slide; do bash $S/wp09r1_evidence.sh r1$k $k video; done
cd $P; git status --short
echo RECORD_DONE
