# 여섯 칸 지휘: 새 악기 소리 3개 (종, 낮은 현, 손북)

여섯 칸에서 새로 쓰는 악기 소리예요. 모두 VCSL(CC0) 고정 커밋 `c1ea7bcc…`에서 가져왔고, `node tools/prepare_samples.mjs`가 다듬어서 `content/audio/`에 놓아요. 다듬는 값(`trim`)은 그대로이고 16비트 모노예요. 기존 13개 파일은 바이트가 그대로예요. 새 파일은 15개, 합쳐서 1.6 MB예요.

## 제안값

`content.json`의 `instruments`는 이 값으로 채우면 돼요. 음 높이 단계 10개(0~9) 모두가 `maxPitchShift` 안에서 풀리는 것은 `tools/lib/new-instrument-samples.test.mjs`가 확인해요.

| 화면 이름 | id | 파일 접두사 | rootMidi | maxPitchShift | gainDb | 파일 수 |
|---|---|---|---|---|---|---|
| 종 | `bell` | `bell_` | 69 (A4) | 2 | -4 | 6 |
| 낮은 현 | `lowstring` | `lowstring_` | 57 (A3) | 2 | 0 | 6 |
| 손북 | `handdrum` | `handdrum_` | 45 (A2) | 5 | +1 | 3 |

`gainDb`는 하프 0, 마림바 -1, 리코더 -4와 같은 크기로 들리게 맞췄어요. 파일마다 맨 위 소리를 -3 dBFS로 맞춰 둔 뒤, 앞 0.5초의 RMS(기존 세 악기의 평균 -16.7 dB)와 A 가중 크기(평균 39.3)를 같게 한 값이에요. 종은 소리가 길게 이어져서 같은 값이면 더 크게 들리므로 -4를 줬고, 손북은 낮은 북이 작게 들리는 것을 감안해 +1을 줬어요. 숫자는 눈으로 맞춘 것이라 귀로 한 번 확인해 주세요.

## 파일과 원본

VCSL 폴더는 종이 `Idiophones/Struck Idiophones/Hand Chimes/`, 낮은 현이 `Chordophones/Composite Chordophones/Strumstick/Finger/`, 손북이 `Membranophones/Struck Membranophones/Conga/`예요. "소리 음"은 YIN으로 잰 실제 높이이고, 오차는 그 음에서 벗어난 센트예요(모두 5센트 안).

| 악기 | 파일 | 소리 음 | 오차 | VCSL 원본 파일 (이름표 음) |
|---|---|---|---|---|
| 종 | `bell_As4.wav` | A#4 | +3.7 | `sus_A#3_r01_main.wav` |
| 종 | `bell_D5.wav` | D5 | +0.5 | `sus_D4_r01_main.wav` |
| 종 | `bell_Gs5.wav` | G#5 | +2.5 | `sus_G#4_r01_main.wav` |
| 종 | `bell_C6.wav` | C6 | +1.4 | `sus_C5_r01_main.wav` |
| 종 | `bell_E6.wav` | E6 | +4.3 | `sus_E5_r01_main.wav` |
| 종 | `bell_Gs6.wav` | G#6 | +4.8 | `sus_G#5_r01_main.wav` |
| 낮은 현 | `lowstring_A3.wav` | A3 | +4.4 | `Strumstick_Finger_Str2_Main_A2_vl2_rr1.wav` |
| 낮은 현 | `lowstring_D4.wav` | D4 | +4.9 | `Strumstick_Finger_Str2_Main_D3_vl2_rr1.wav` |
| 낮은 현 | `lowstring_G4.wav` | G4 | +3.3 | `Strumstick_Finger_Str3_Main_G3_vl2_rr1.wav` |
| 낮은 현 | `lowstring_A4.wav` | A4 | +0.9 | `Strumstick_Finger_Str3_Main_A3_vl2_rr1.wav` |
| 낮은 현 | `lowstring_D5.wav` | D5 | +2.4 | `Strumstick_Finger_Str3_Main_D4_vl2_rr1.wav` |
| 낮은 현 | `lowstring_G5.wav` | G5 | +3.1 | `Strumstick_Finger_Str3_Main_G4_vl2_rr2.wav` |
| 손북 | `handdrum_A2_tumba.wav` | (A2로 표시) | 0 | `Tumba_HitN_v4_rr1_Sum.wav` |
| 손북 | `handdrum_A3_conga.wav` | (A3로 표시) | 0 | `Conga_HitN_v3_rr1_Sum.wav` |
| 손북 | `handdrum_A4_quinto.wav` | (A4로 표시) | 0 | `Quinto_HitN_v3_rr2_Sum.wav` |

## 알아 둘 점

- **VCSL 이름표는 실제 소리보다 한 옥타브 낮아요.** 마림바와 리코더처럼 손 차임과 스트럼스틱도 그래요(잰 값으로 확인). 그래서 "낮은 현"의 이름표 A2~G4는 실제로 A3~G5로 울려요. 스트럼스틱에서 안정적으로 잡히는 가장 낮은 소리가 D3(147 Hz)라서, 낮은 현의 `rootMidi`는 45가 아니라 57이에요. 더 낮추고 싶으면 `Str1_Main_D2`(실제 D3)를 더하고 `rootMidi` 45, `maxPitchShift` 5로 바꾸면 되는데, 맨 아래 A2는 D3를 5반음 내려 재생해야 해요. `Str1_Main_F#2`, `G2`는 기본음이 둘째 배음보다 40 dB쯤 약해서 측정이 이름표보다 두 옥타브 높게 나오므로 쓰지 않았어요.
- **손북은 음 높이가 없는 소리예요.** 설정에 `"unpitched": true`를 두면 음 높이 측정을 건너뛰고, 매니페스트에는 `unpitched: true`, `measuredHz: null`, `centsFromLabel: 0`이 들어가요(그래서 `tuneCents`는 0). 이름표 음은 A로 두었고, 가장 낮은 북(툼바)이 A2, 콩가가 A3, 가장 높은 북(퀸토)이 A4예요. 단계 0~2는 툼바, 3~7은 콩가, 8~9는 퀸토가 울리고, 재생 속도로 음 높이를 ±5반음(속도 0.75~1.34배)까지 바꿔요.
- **2500 Hz 한계는 이번 소리에서 넘지 않아요.** 가장 높은 소리가 종의 G#6(1666 Hz)예요. 그래도 같은 일이 생겨도 조용히 틀린 값이 들어가지 않도록, 이름표 음에서 50센트 넘게 벗어난 소리는 `prepare_samples.mjs`가 멈추고, 설정에서 소리마다 `fmin`/`fmax`(Hz)를 넓힐 수 있게 했어요.
- 종 `bell_As4`는 소리가 천천히 커져서 첫 소리가 22.5 ms 늦게 들려요(`perceivedOnsetOffsetMs`로 `content-build`가 보정해요).

## 들어서 확인할 점

1. 종: 옮겨 재생하는 단계(한 반음은 0, 4, 5, 9단계, 두 반음은 1, 3, 7단계)가 둔하게 들리지 않는지, 하프와 같은 음역(A4~G6)이라 소리가 겹쳐 묻히지 않는지, 1.3초에서 잘리는 꼬리가 어색하지 않는지.
2. 낮은 현: 맨 아래 A3(220 Hz)가 "낮고 묵직하게" 들리는지. `lowstring_A3`는 둘째 배음(441 Hz)이 기본음(221 Hz)보다 27 dB쯤 크게 녹음돼 있어서, 높이는 A3로 느껴져도 속이 빈 듯 얇게 들릴 수 있어요.
3. 손북: 북이 바뀌는 단계(3과 8)에서 소리 색이 갑자기 달라져도 어색하지 않은지, 툼바를 올려 재생한 단계 1과 2가 얇게 들리지 않는지.
4. 소리 크기: 같은 악기 안에서 파일마다 차이가 있어요(앞 0.5초 RMS 기준 종 7.7 dB, 낮은 현 5.1 dB, 손북 2.9 dB; 하프는 7.1 dB). 높은 음이 작게 들리는 단계가 있는지.
