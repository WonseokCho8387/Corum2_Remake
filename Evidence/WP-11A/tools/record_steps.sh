#!/usr/bin/env bash
# WP-11 Part A recorded run on the clean commit: EditMode XML, then the WP11_Evidence main run with video.
W=C:/Users/wscho/AppData/Local/Temp/claude/65e40243e0f06e30/e88d8a76-fd3b-4580-95f3-dd329e162832/scratchpad
source $W/uhelpers.sh
P=/d/ProjectR/Corum2Remake
cd $P; git status --short; git log --oneline -1
wait_editor
unity command test run --mode EditMode --filter Corum2 --timeout 400 $C --format json > $W/wp11a_tests.json 2>&1
grep -m1 -A5 '"Summary"' $W/wp11a_tests.json
cp /c/Users/wscho/AppData/LocalLow/DefaultCompany/Corum2Remake/TestResults.xml $W/wp11a_TestResults.xml
rm -rf $P/Diagnostics/WP11
python $W/wp11_gen.py
bash $W/wp11_run.sh main $W/wp11a_main.txt Assets/Corum2/Scenes/WP11_Evidence.unity video WP11
cd $P; git status --short
echo RECORD_DONE
