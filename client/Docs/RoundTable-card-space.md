# 원탁 카드 공간 이미지 수정

- 도구: 내장 image_gen 이미지 편집, 투명 배경 옵션 사용.
- 적용 이미지: `Assets/Resources/DayTable/RoundTable.png` (1536 × 1024, RGBA).
- 원본 보관: `Docs/RoundTable-before-card-space.png`.
- 테이블 둘레의 동전과 소품을 제거하고 나침반과 랜턴 묶음을 지도 양옆 안쪽으로 축소 배치.
- 지도 중앙은 투표 카드 공개용으로 사용 가능. 이미지 자체에는 카드나 자리 번호를 그리지 않음.
- 기존 FrontRim은 수정 전 동전이 다시 표시되지 않도록 런타임 중복 렌더링 제거. 원본 리소스 파일은 보존.
- 이번 작업은 카드 배치 공간을 위한 이미지 수정이며 투표 카드 공개 로직은 추가하지 않음.

## 최초 편집 프롬프트

```text
Use case: precise-object-edit.
Asset type: Unity 2D game sprite, a single pirate round table, transparent PNG.
Input image 1 is the original table EDIT TARGET. Input image 2 is a layout REFERENCE ONLY; its colored markup and writing MUST NOT appear in the output.
Edit the original table to match the reference's intended cleared spaces. Preserve the original rich hand-painted cartoon fantasy style, blue wood and gold brass trim, exact elliptical perspective, camera, 1536x1024 landscape canvas, and original table silhouette position: tabletop outer ellipse approximately x48..1488,y158..701; barrel body down to y932, golden anchor badge and ropes unchanged.
MOST IMPORTANT: clear the entire wide annular band around the tabletop (green in reference) for 12 dynamically placed voting cards. Remove ALL loose coins, coin stacks, raised tokens, scrolls and equipment from this outer band. Restore continuous flat clean blue wooden tabletop with subtle existing gold engraved navigation lines. The band should be at least about 110 px wide along the left/right and 75px at front/back, uninterrupted all the way around.
Inside this band, retain the large parchment treasure map. Provide a broad uninterrupted low-contrast central part of the map approx x490..1040,y320..515 as a usable area where 12 cards can later be laid out in two rows. Keep faint map drawing in this area, no raised objects or card drawings.
Rearrange the existing props into TWO tiny compact tasteful clusters INSIDE the inner oval ONLY, avoiding both the clear band and central map. A small flat brass compass with just a few coins near the left inner edge around x360,y385; on the right inner edge near x1160,y395 put a mini warm brass lantern, a short green bottle and a small horizontal spyglass/one rolled map. Dramatically reduce original prop sizes to fit. Every part including the TOP of the lantern must project below y270 and entirely inside the inner ellipse (rough bounds x265..1270,y245..600). Nothing protrudes over the back rim. Central map stays open. No objects at front edge.
All table and prop contours must be crisply cut out on actual transparent alpha. No black/brown backdrop, no haze or exterior glow, no shadow plane. Keep full table and margin, no cropping. No characters, chairs, cards, slot boxes, numerals, labels, colored guide lines, overlays, annotations or text. Output only the finished game asset.
```

## 최종 보정 프롬프트

```text
Use case precise-object-edit. This is a refinement of the attached transparent 1536x1024 pirate table game sprite. Preserve everything (table shape, canvas alignment, materials, map and LEFT compass) exactly, change ONLY the small RIGHT group consisting of lantern, green bottle, telescope and scroll. The top of the lantern is still intruding into the empty outer card band. Shrink this entire right group to about 70% of its current size and move the group DOWN onto the right edge of the parchment map. Fit all four objects fully inside rectangle x1050..1210,y335..470, including lantern top and scroll ends, no part above y330 or right of x1220. Keep the central map rectangle x500..1020,y310..520 free from objects. The entire blue outer ring must be completely clear for player cards. No new objects, coins or markers. Do not draw cards, guides, numbers or text. Preserve the original table body and the existing transparent alpha background. Output exactly one transparent game sprite with same canvas and table bounds.
```

## 투표 카드 연결 (`DayTableVoteCards`)

- 카드 자리: 테이블 윗면 바깥 띠, 자리마다 하나(`DayRoundTableView.CardSpotFor(playerId)`). 위 이미지에서 12곳 모두 비어 있음을 확인함.
- 투표 중: 서버가 내 표를 받으면 내 화면에서만, 카드가 내 앞 자리에 놓였다가 대상의 자리로 던져진다. 다시 투표하면 새 대상으로 옮겨 가고, 대상이 게임에서 나가 표가 지워지면 카드를 거둔다.
- 처형: 공개된 처형 결과(`ExecutionResultDto.votes`)의 득표 수만큼 각 대상 자리에 엎어 둔 카드 더미와 "n표"를 보여 준다. 내 카드는 그 더미 맨 위에 남는다.
- 누가 누구에게 냈는지는 그리지 않는다(다른 사람 표는 처형 때 공개되는 수로만 보인다).
- 테이블은 처형(EXECUTION) 동안에도 보이고, 다음 단계(밤)에서 카드가 치워진다.
