# P1 증빙 패킷 (Claude, 2026-11-14)

P1 전체 묶음(P1-A~D)의 실행 증빙입니다. 판정 기준은 [완화 테스트 범위](../../../Corum2/docs/GPT/CORUM2_P1_00_TEST_MATRIX_Claude_v1.0.md)를 따릅니다. 아래 결과는 제작자 실행 결과이며 GPT 판정이 아닙니다.

## EditMode

- `editmode/TestResults_441.xml`: Unity Test Framework 결과, **441/441 Passed**
  - P0 315개 회귀
  - P1 126개: `P1GrowthTests` 26, `P1DamageTests` 13, `P1MagicTests` 42, `P1SystemTests` 27, `P1CoverageTests` 13, `P1ReviewFixTests` 5 (GPT 중간 대조 반영)
- 테스트 소스 커밋: `42f512b` (main)
- 테스트 이름에 TC 번호가 들어 있습니다(예: `T05_T58_T62_...`). 한 테스트가 여러 TC를 덮을 수 있습니다.

## Play 스모크 (P1_Sandbox)

- 실행: `play/RUN-20261114-232740.*`
  - 소스 `1662d05`, `working_tree_dirty: false`
  - 장면: `Assets/Corum2/Scenes/P1_Sandbox.unity`
  - 빌더: `Corum2/P1/Build Sandbox`
- 로그: `play/RUN-20261114-232740.ndjson.gz` (gzip). `commands.txt`에는 마지막 스크립트만 남습니다. 모든 스크립트 명령은 로그의 `marker` 이벤트(`P1Script:*`, `TestCase`)로 기록돼 있습니다.
- 검사: `python Tools/p1_play_checks.py <run.ndjson>` → `checks/p1_play_checks.json`. 8개 항목 모두 `pass: true`입니다.

| 시나리오 (test case id) | 내용 | 캡처 |
|---|---|---|
| P1-PLAY-A-GROWTH | 적 3마리 처치 → EXP 130 → 첫 안전 경계에서 성장 화면(필수 매직 미사일 포함), A로 선택 | `p1_growth_screen.png` |
| P1-PLAY-A-MISSILE | 매직 미사일 커맨드 →↑←+X, 투사체 5, 유도 3장. 시작 시 미소비, 커밋 시 1회 소비 | `p1_missile_fan.png` |
| P1-PLAY-B-SHOP | 상인 근처에서 ↑ → 상점, 영구팩 3장 표시(미결제) | `p1_shop.png`, `p1_shop_pack.png` |
| P1-PLAY-T204 | 카드팩 표시 중 처치 레벨업 → 상점 강제 종료·팩 무료 취소 → 성장 우선 | `p1_t204_preempt.png` |
| P1-PLAY-T201 | 링 열림 + 같은 프레임 처치 EXP와 치명 피해 → 사망 보류, 링 강제 닫힘, 저장·로드 후 같은 후보, 선택 → 전회복 생존 | `p1_t201_held.png`, `p1_t201_after.png` |
| P1-PLAY-T202 | Lv48 → 한 번에 2레벨, 초과 30 → Lv50 재화, 대기 중 상점 거절, 완료 후 Lv50 재화로 구매, 체크포인트 로드로 함께 복원 | `p1_t202_shop_lv50.png` |
| P1-PLAY-T203 | 지역 이탈 확인(취소 → 유지, 확인 → 상인 퇴장), R2 롤 성공 후 스폰 막힘 → 오류 상태, 누적 불변, 재진입 재추첨 없음 | `p1_t203_exit_confirm.png`, `p1_t203_reentry.png` |
| P1-PLAY-SPELLS | 화이어 월(추가 벽), 대천사, 매직 실드, 빛의 구(연쇄) 커맨드 시전 | `p1_spells.png` |

## TC 상태

`python Tools/p1_tc_coverage.py <xml> <play_checks.json> <out>` → `checks/p1_tc_status.json`

| 상태 | 수 |
|---|---:|
| PASS (자동 부분) | 203 |
| HUMAN_REVIEW (T45 명중률 통계) | 1 |

PASS 중 26개는 마스터 미정(S), 실기기(E), 플레이 측정(H) 하위 조건을 `openSubConditions`에 따로 남깁니다. 이 하위 조건은 PASS에 포함하지 않습니다.

## 알려진 한계

- 시험 씬의 상인 출현 확률은 화면 확인을 위해 1.0으로 올렸습니다. 기본값과 마스터 값은 0.15입니다.
- 스폰 막힘(T203)은 실행 중 `Regions[1].SafeSpawns[0].Blocked = true`로 설정했고, 로그 marker `TestSetup:R2SafeSpawnBlocked`로 남겼습니다.
- 위 이동 지점 위에서 →↑←(매직 미사일)를 입력하면 맵 이동도 함께 일어납니다. P0 CF-01(맵 지점과 커맨드의 우선순위)이 아직 미정이라 그대로 두었습니다.
- 디버그 HUD 글자가 P1 패널 위에 겹쳐 보입니다. GameHost `showHud`를 끄면 사라집니다.
