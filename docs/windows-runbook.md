# 들판 오케스트라 Windows 실행 순서표 (RUNBOOK)

목적: 전시와 같은 조건(화면 2대, 로그인하면 저절로 시작, 꺼지면 다시 켜짐)으로 돌려 보고, 무엇이 되고 안 되는지 기록을 남겨요. 이 문서는 사용자가 혼자 따라 하는 순서표예요. 표에는 **언제 했는지(시:분)만** 적으면 돼요. 나머지는 프로그램이 `logs` 폴더에 시각과 함께 기록하고, Claude가 그 기록으로 복구 시간을 계산해요.

한 줄 요약: **설치 → 감시 프로그램으로 켜기 → 화면 2대 확인 → 고장 6가지 일부러 내기 → 밤새 켜 두기 → 결과 zip 보내기.**

곡 모드가 든 새 판에서는 **세션 E(곡 모드 확인, 진짜 손, 약 15분)** 도 해 주세요. Tab으로 여는 설정 패널이 처음 나오는 곳이에요.

## 0. 먼저 알아 둘 것

**바꾸는 것과 안 바꾸는 것** (관리자 권한은 어디서도 필요 없어요)

| | |
|---|---|
| 바꾸는 것 (물어보고 해요) | 내 계정의 시작 프로그램 폴더에 바로가기 1개 (`install-autostart.ps1`, Y 를 눌러야 만들어요). 지우기는 `uninstall-autostart.ps1`. |
| 바꾸지 않는 것 | Windows 설정, 전원·절전 설정, 레지스트리, 작업 스케줄러, 자동 로그인, 방화벽, PATH. |
| 앱이 하는 것 | 앱이 켜져 있는 동안만 Windows에 "잠들지 마세요"라고 부탁해요. 앱을 끄면 부탁도 사라져요. |

**그래픽 카드가 따로 없는 노트북(Intel 내장 그래픽 등)이면 `-Backend msmf` 를 붙여요. 화질은 `-Quality` 를 안 주면 앱이 알아서 맞춰요(아래 "자동 화질"); 고정하려면 `-Quality Low` 를 더해요.** 아래 3번 명령의 `$extra` 에 `@('-Backend', 'msmf')` 라고 적어요. 그래픽 카드가 따로 있는 PC는 붙이지 않아도 돼요. 손 추적의 카메라 방식 기본값(auto)은 그대로 두었고, 이 옵션을 줄 때만 바뀌어요. 이유는 i7-10610U 노트북(Intel UHD 그래픽)에서 잰 값이에요.

- `-Quality Low`: 앱 화질을 낮춰요(풀이 약 6분의 1). 기본(High)으로 켰더니 그래픽이 96% 로 꽉 차서, 손 추적이 사진 한 장을 읽는 시간이 16 ms 에서 110~420 ms 로 늘었고 음이 하나도 나지 않았어요.
- `-Backend msmf`: 웹캠을 Media Foundation 방식으로 열어요. 기본 방식(DirectShow)은 이 웹캠에서 1초에 15장만 왔는데, 앱이 손의 속도를 재는 구간(0.07초)에 딱 걸리는 숫자라서 음이 잘 나지 않아요. msmf 는 1초에 30장이 왔어요. msmf 로 카메라가 안 열리면 `-Backend` 를 빼 보세요.
- 같은 내용이 손 추적 설명서(`README-hand-setup.txt` 4절)에도 있어요. 로그인하면 저절로 시작하게 만들 때도(세션 C) 같은 옵션을 적어야 해요. 바로가기에는 적은 옵션만 들어가요.

**감시 프로그램이 하는 일.** 손 추적과 앱을 같이 켜요. 둘 중 하나가 꺼지면 3초 뒤 다시 켜요. 앱이 5초마다 쓰는 "살아 있어요" 파일(`heartbeat.txt`)이 30초 넘게 멈추면(앱이 얼었다는 뜻) 앱을 끄고 다시 켜요. **손 추적도 자기 "살아 있어요" 파일(`tracker\logs\tracker-heartbeat.txt`)을 2초마다 쓰고, 45초 넘게 멈추면 다시 켜요**(카메라를 읽는 중에 돌아오지 않는 경우, 예를 들어 USB를 뽑았을 때예요. 카메라가 없어서 다시 열어 보는 중이면 계속 쓰니까 다시 켜지 않아요). 앱은 **켜지는 순간부터** 이 파일을 써요. 소리 장치가 없으면 파일의 `audio=` 가 `starting`(소리를 기다리는 중), 30초 뒤 `failed`(포기)가 되고, 켜진 뒤에 장치가 사라지면 `stalled`(소리 시계가 3초 넘게 멈춤)가 돼요. `failed` 와 `stalled` 가 20초 넘게 이어지면 앱을 다시 켜요. **소리 시계가 돌아도 앱이 떴다는 뜻은 아니에요**(켜지다가 오류로 멈춘 앱은 소리 시계가 계속 가요): 그래서 파일에 `app=loading`/`running`/`failed` 줄이 따로 있고, 켠 지 180초가 지나도 `app=loading` 이면(`app_not_ready`) 또는 `audio=starting` 이면(`audio_not_started`) 다시 켜요. `app=loading` 인 동안은 멀쩡한 시작으로 치지 않아서 쉬는 시간이 되돌아가지 않아요. 앱은 켜지는 중에 오류가 나면 스스로 `app=failed` 로 바꾸고 이유를 `Player.log` 에 `[Downbeat] boot: …` 로 남겨요. 180초는 잰 값이 아니라 여유예요: Mac(Apple M4 Pro)에서 새로 복사한 앱을 켰더니 첫 "살아 있어요" 가 1.2초, `audio=ok` 가 1.3초, 준비 완료가 1.8초에 나왔어요(5번, `measurements/day5/boot-to-first-heartbeat.json`). Windows 노트북에서는 재 보지 못했어요. 첫 "살아 있어요" 가 90초 넘게 걸린다면 알려 주세요. 10분 안에 5번 넘게 다시 켜야 하면 60초 쉬었다가 이어서 해요. **소리 장치가 없어서 켜는 족족 실패하면 쉬는 시간이 점점 길어져요:** 켠 지 2분을 못 채운 시작이 3번 연달아 나오면 쉬는 시간이 60초, 120초, 240초, 480초로 두 배씩 늘다가 600초(10분)에서 멈춰요(그동안에도 계속 시도해서 장치가 돌아오면 최대 10분 안에 살아나요). 2분 이상 멀쩡하게 돈 시작이 한 번 나오면 처음으로 돌아가요. 이때 `logs\supervisor-날짜.csv` 에 `backoff_escalated`(늘어나는 중)와 `recovered`(돌아왔어요) 줄이 남아요. 감시 프로그램이 받는 앱·손 추적의 화면 밖 출력 파일(`logs\<이름>.out.txt`)은 **켤 때마다** 5 MB 가 넘으면 `.1` `.2` `.3` 으로 밀려 나가요. **Windows 에서는 켜져 있는 동안에는 이 파일을 비우거나 자르지 않아요**(앱이 쓰는 도중에 비우면 다음 글이 옛 자리에 써져서 앞이 0으로 채워지고 기록이 사라져요. Mac 에서는 1분마다 자르니 총 20 MB 이하예요). 그래서 Windows 에서는 한 번 켠 동안 이 파일이 계속 커질 수 있고, 다시 켜질 때 밀려 나가요. 이 파일에는 화면 밖으로 나온 예상 밖의 글만 쌓이고(손 추적의 자기 기록 `tracker\logs\tracker-supervised.log` 는 따로 5 MB 가 넘으면 옛 파일 3개까지 돌려 써요) 계산하면 초당 100바이트씩 쓰는 오류 반복이 하루에 약 8.6 MB 예요(잰 값이 아니라 계산이에요). 디스크가 걱정되면 감시 프로그램을 끄고 이 파일을 지우거나 옮겨 주세요. 손 추적은 손 인식 모델(약 8 MB)을 처음 켤 때 내려받는데, 그동안에도 "살아 있어요" 파일(`camera=starting`)을 계속 써서 감시 프로그램이 멈춘 것으로 보고 끄지 않아요(내려받는 조각마다 써요). 그래도 **먼저 `setup-hand-tracker.ps1` 를 한 번 돌려 두는 게 좋아요**: 설치가 모델을 미리 받아 두어서 전시 때는 인터넷이 필요 없어요. **화면이 까맣거나 깨진 것, 화면이 엉뚱한 모니터에 뜬 것, 카메라가 얼어붙은 같은 그림을 계속 보내는 것은 프로그램이 알아채지 못해요.** 그건 눈으로 봐 주세요.

**앱이 스스로 끄는 경우 (앱이 얼었을 때, 그래픽 장치를 잃었을 때).** 2026-11-03 밤샘에서 그래픽 드라이버가 시간 초과로 다시 시작되자 (`Player.log` 에 `d3d11: failed to create buffer … [0x887A0005]`, 그래픽 장치를 잃었다는 뜻이에요) 앱이 꺼지지도 되살아나지도 않고 아침까지 멈춰 있었어요(`docs/windows-soak-2026-11-02.md`). 그래서 앱이 두 가지를 스스로 봐요. ① **20초 감시(믿을 수 있는 쪽)**: 앱이 뜬 뒤(`app=running`) 20초 동안 장면을 한 장도 못 그리면(앱의 메인 스레드가 얼었다는 뜻이에요) 앱이 이유를 한 줄 적고 스스로 끝나요. ② **로그 갈고리(받으면 바로)**: 앱의 로그 갈고리가 장치를 잃었다는 말(`0x887A0005`, `0x887A0006`, `0x887A0007`, `DEVICE_REMOVED`, `DEVICE_HUNG`, `DEVICE_RESET`)이 든 줄을 받으면 20초를 기다리지 않고 바로 끝나요. **다만 Unity 가 자기 줄(`d3d11: …` 같은 것)을 갈고리를 거치지 않고 `Player.log` 에 바로 쓸 수도 있어요. 갈고리가 그 줄을 받는지는 확인하지 못했어요. 받지 못하면 ①의 20초 감시가 끝내요.** 끝날 때의 숫자(종료 코드)로 어느 쪽인지 알 수 있어요: **71 = 갈고리가 장치를 잃었다는 줄을 받았어요, 72 = 메인 스레드가 20초 넘게 멈췄어요(20초 감시)**. 앱이 얼린 뒤 늦어도 23초(20초 + 감시의 한 번 보기 1초 + 기록 쓰기 1초 + 로그 쓰기 1초)에 끝나요. 감시 프로그램과 같이 돌리면 `logs\supervisor-날짜.csv` 에 `exit` 줄과 `code=71`/`code=72` 가 남고 3초 뒤 다시 켜져요(감시 프로그램이 30초 묵은 heartbeat 로 다시 켜기 전이에요: 앱이 얼고 25~30초 뒤). 혼자 돌리면 멈춘 채로 아침을 맞지 않고 꺼져요. 이유는 `Player.log` 의 `[selfexit] reason=…` 줄과 `logs\exit-reasons.txt` 에 남아요. 20초는 밤샘에서 가장 길었던 장면 하나(0.87초)의 20배가 넘는 값이에요. **안 끝내는 때:** 켜지는 중(`app=loading`)에는 멈춰 있어도 끝내지 않아요(그건 감시 프로그램의 180초 여유 몫이에요). 컴퓨터가 잠들었다 깬 것은 멈춤으로 치지 않고, 깬 뒤 첫 장면이 그려질 때까지 기다려요. 단 깬 뒤 40초(한도의 2배) 동안 한 장도 안 그려지면 끝내요(깨는 중에 그래픽 드라이버가 다시 시작되는 경우예요). **끄는 중에 멈춘 것:** 끄기 시작한 뒤 20초 안에 안 끝나면 같은 감시가 72 로 끝내요. 다만 이것은 감시 스레드가 살아 있는 동안만이에요. Mono 가 끝낼 때 이 스레드를 먼저 멈추면, 그래픽 장치를 거두는 마지막 단계에서 멈춘 것은 못 잡아요. `Player.log` 에 `[selfexit] quitting: …` 줄은 있는데 `the watchdog thread ended` 줄도 `exit-reasons.txt` 도 없이 멈췄다면 그 경우예요. **끄는 법:** 앱 옵션 `--stall-exit-sec 0` (장치를 잃었을 때 끝나는 것까지 모두 꺼져요). 시간을 바꾸려면 `--stall-exit-sec 30` 처럼 초를 줘요: 2~3600 초이고, 범위 밖이면 가까운 끝으로 맞추고 `Player.log` 에 경고를 적어요. 에디터에서는 아무 때도 스스로 끝나지 않아요. **Windows에서 확인할 것:** 가짜 시계와 가짜 종료로만 시험했고, Windows 에서 그래픽 장치를 실제로 잃게 해 본 적은 없어요(Mac 에서는 만들 수 없어요). 다음 밤샘이 끝난 뒤 ⓐ `exit-reasons.txt` 가 생겼는지, 있다면 71 인지 72 만 있는지, ⓑ `Player.log` 의 `d3d11:` 줄 끝에 `(Filename:` 로 시작하는 꼬리가 붙어 있는지 알려 주세요(붙어 있으면 Unity 의 기록 장치를 거친 줄이라 갈고리가 받았을 가능성이 커요).

**자동 화질(기본): 노트북과 면접 데모는 그냥 켜요.** `-Quality` 를 주지 않거나 `-Quality Auto` 로 켜면 앱이 화질을 스스로 맞춰요. 낯선 PC에서도 아무도 설정을 고르지 않아도 돼요. ① **시작**: 그래픽 장치 이름으로 첫 단계를 골라요. 이름에 `Intel`, `UHD`, `HD Graphics`, `Iris`, `Vega` 같은 말이 있는 내장·약한 그래픽(그러면서 `Arc`, `GeForce`, `RTX`, `Radeon RX`, `Apple M` 같은 말이 없는 것)이거나 그래픽 메모리가 2048 MB 미만이면 Low 로(사다리의 `Low@0.7` 칸: 메모에서 약한 노트북을 잰 곳이에요), 아니면 High 로 시작해요(말 목록은 `content.json` 의 `autoQuality.start`). 켠 뒤 8초 동안의 프레임으로 다시 확인해요(그동안은 3초마다 한 단계씩 내려갈 수 있어요). 이유는 Player.log 의 `[quality] automatic ... starts at step ...` 줄에 남아요. **규칙은 이렇게 단순해요: 등급(High 나 Low)은 시작할 때 정해지고, 실행 중에는 더 좋은 쪽으로 올라가지 않아요.** 더 싼 등급으로는 많아야 한 번 내려가요. 같은 등급 안에서는 그림 크기 비율이 오르내려요. (대가: 시작을 Low 로 한 중간 PC 는 그 실행 내내 Low 예요. High 를 다시 보려면 앱을 다시 켜거나 `-Quality High` 로 고정하세요.) ② **사다리**(가벼움 → 쌈): High@1.0 → High@0.85 → Low@1.0 → Low@0.85 → Low@0.7 → Low@0.6 (`등급@그림 크기 비율`; High 는 60장/초, Low 는 30장/초 상한을 목표로 해요). ③ **내려가는 때**(한 번에 한 단계, 앞 변경에서 10초 이상 지난 뒤; 등급을 바꾸는 단계는 한 번만): 최근 4초의 절반 넘는 시간이 목표 프레임 시간의 1.3배를 넘는 프레임에 쓰였거나, 손 추적이 1초에 12장 아래로 8초 넘게 오는데 앱이 바쁠 때(그래픽 시간을 재면 그 단계 예산의 절반 넘게 쓸 때, 재지 못하면 늘 바쁘다고 봐요). ④ **올라가는 때**(그림 크기 비율만, 같은 등급 안에서): 한 단계에 90초 넘게 머물렀고, 최근 4초에 느린 프레임이 하나도 없고, 그래픽 시간이 재어지면 예산의 절반 아래이고, **연주 중에 본 프레임으로 20초 넘게 여유를 확인했을 때**(느린 프레임 2% 이하)만 한 단계 올라가요: 아무도 없는 들판의 가벼운 프레임만으로는 올라가지 않아요(손이 있을 때가 훨씬 무거우니까요). 올라가고 나서 **5분(`upHoldSec` 300초) 안에 다시 내려오면 그 시도는 실패**로 세고, 다음 시도는 두 배 늦게(90초 → 180초 → 360초 → 720초 → 1440초, 최대 30분) 해요. 20초 넘게 연주를 보여 줬더라도 5분이 안 됐으면 마찬가지예요(20초만 버티면 '버텼다'고 치던 때는, 올라간 20초 뒤에 느려지는 기계가 2분마다 같은 두 크기를 오르내렸어요). 올라간 단계는 **거기서 5분 넘게 머물렀고 연주가 20초 넘게 이어졌을 때만** '버텼다'고 치고 그제야 실패 기록을 한 번 용서해요(시계만으로는 용서하지 않아요: 빈 들판에서 몇 시간이 지나도 그대로예요). 이 규칙이 보장하는 것: 올라간 뒤 5분 안에 매번 내려오는 기계(부하가 한 단계의 예산 바로 위아래에 걸린 기계)는 시도 사이가 두 배씩 늘어서 결국 30분에 한 번만 올라갔다 내려와요. 5분을 넘게 버티다 내려오는 기계는 실패로 치지 않아서 대기가 늘지 않으니, 5분 + 대기(최소 90초)마다 한 번까지 오르내릴 수 있어요. 또 OneDrive 같은 일시적인 급증 뒤에는 **방문자가 한동안 연주할 때까지 Auto 가 낮은 그림 크기에 머물 수 있어요. 일부러 그렇게 했어요.** ⑤ **풀·꽃을 다시 짓는 등급 바꿈(High → Low, 내려가는 쪽으로만, 한 번)은 들판이 한가할 때(손이 없고 가라앉은 뒤)만** 해요. 연주 중에는 그림 크기 비율만 먼저 내려가요. 그래도 프레임이 느리면 사다리의 값보다 더 아래로(바닥 0.5까지) 10초마다 0.1씩 계속 내려가요: 방문자가 떠날 때까지 느린 채로 두지 않아요. 등급 바꿈은 손이 나타난 그 프레임에는 하지 않아요(풀을 다시 짓느라 한 프레임이 길어지니까요). 그림 크기 비율은 0.5초에 걸쳐 조금씩(0.025씩) 바꿔서 화면이 튀지 않아요. ⑥ **보이는 곳**: 바뀔 때마다 Player.log 에 `[quality] step down High@1.00 -> High@0.85: ...` 같은 줄(이유와 숫자 포함), 상태 CSV 맨 끝 네 칸 `render_scale`, `quality_level`(**지금 적용 중인 사다리 단계**예요. 등급을 내리는 것이 연주 때문에 기다리는 동안에는 옛 단계 그대로이고 `render_scale` 만 사다리 값 아래로 내려가요. 방문자가 떠나서 등급이 바뀌면 새 단계로 뛰어요), `starve_ignored`(손 추적의 굶음을 무시 중이면 1), `quality_changes`(그 1분에 화질을 바꾼 횟수: 등급 바꿈은 풀을 다시 짓느라 한 프레임이 길어서, 8시간 분석에서 앱이 멈춘 프레임과 가를 수 있어요). ⑥-2 **손 추적이 굶는 것을 앱 탓으로만 보지 않아요.** 손 추적 때문에 내려간 단계는 **보답이 있어야** 해요: 20초 안에 손 추적이 1초에 받는 장 수가 20% 넘게 올라야 해요(20%는 10장/초에서 3초 창에 든 장 수의 흔들림의 표준편차의 약 3배, 20초는 내려가는 대기 2번이에요. 모두 `autoQuality.starve*` 에 있어요). 연달아 2번 내려가도 오르지 않으면 카메라가 낮은 장 수로 멈춰 있거나 손 추적이 CPU에 묶인 것이니 **앱 탓이 아니라고 보고 10분 동안 무시해요**. 그동안은 프레임 시간만으로 판단하고(그림 크기가 다시 올라갈 수 있어요), 끝난 뒤에 다시 실험을 한 번 하고 또 실패하면 **무시 시간이 두 배**(10분 → 20분 → … 최대 4시간)가 돼서, 8시간 동안 같은 헛수고를 되풀이하지 않아요. 실험하는 20초 사이에 손 추적이 멈췄거나(실행 중이 아님) 장이 2초 넘게 안 오면(다시 시작, 카메라 끊김) 그 실험은 **취소**예요(그 한 번은 성공도 실패도 아니에요). 다만 **취소가 연달아 3번이면(`starveCancelsAsFailure`) 실패 1번으로 쳐요**: 몇 초마다 끊기는 카메라 때문에 취소된 실험만 되풀이하며 사다리를 끝까지 내려가는 일이 없도록요. 그래서 위의 "연달아 2번"은 끝까지 간 실험이 오르지 않은 것과 취소 3번 묶음을 같은 한 번으로 세고, 취소만 6번 이어져도 10분 무시가 시작돼요. 끝까지 간 실험이 한 번 나오면(오르든 오르지 않든) 취소 횟수는 처음부터 세요. 보답이 있었는지와 무시를 시작하고 끝낸 것은 Player.log 의 `[quality]` 줄에, 무시 중인지는 CSV 의 `starve_ignored` 칸에 남아요. 손 추적 쪽의 시간(추론 시간)으로 가르지 않는 이유: 이 노트북에서는 앱의 그래픽 부하만으로도 같은 전력을 나눠 써서 추론이 16 ms 에서 110~420 ms 로 늘었어요.
⑦ **끄는 법**: `-Quality High` 또는 `-Quality Low` 로 고정하면 자동은 꺼져요(`quality_level` -1). `--render-scale` 이나 `--max-fps` 만 주면(예: `-MaxFps 30`) 그 숫자만 고정되고 등급은 Auto 가 골라요(시작 추정과 실행 중 규칙으로 고르고, 고정한 숫자는 건드리지 않아요; 그림 크기를 고정하면 사다리는 등급당 한 칸이고, 로그에도 그림 크기를 말하지 않아요). `-Quality High|Low` 와 함께 주면 모두 고정이에요. 내용 파일에서 `autoQuality.enabled` 를 false 로 해도 꺼져요. `--quality-level N` 은 사다리의 N번째에서 시작해요(측정용). **전시 PC 는 Auto 를 쓰지 말고 `-Quality High` 나 `-Quality Low` 로 고정하세요. Auto 는 노트북과 데모용이에요.** (한 번 Auto 로 켜 보고 어느 단계에 머무는지 본 다음 그 등급으로 고정하세요.) 화면이 켜져 있는 동안 바뀌지 않는 것이 전시에는 더 좋아요(자동 화질은 부하에 따라 그림이 조금씩 달라져요). **그림 크기 비율은 들판을 그리는 카메라 하나에만 걸려요**(어느 화면에 두었든: `meadowIndex` 1 / `recordIndex` 0 으로 바꿔도 기록 화면이 작아지지 않아요): 기록 화면과 캡처는 언제나 1.0 으로 그려서 글자가 또렷해요. 손 추적 카메라는 그대로 `-Backend msmf` 를 권해요. **Windows에서 확인할 것:** Auto 가 노트북에서 어느 단계에 머무는지(`[quality]` 줄과 `quality_level` 칸), 그러면 `tracker_fps` 가 오르는지, 바뀌는 순간 눈에 띄는 변화가 있는지, 그래픽 시간(`--log-frame-stats` 의 `gpu_ms`)이 D3D11 에서 재어지는지(Mac(Metal)에서는 재어지지만 그래픽 칩의 클럭이 부하에 따라 바뀌어서 값이 흔들려요; 재어지지 않으면 프레임 시간만으로 판단해요).

**느린 노트북에서: Low 단계가 하는 일.** 그래픽이 따로 없는 노트북은 앱이 그림을 그리느라 그래픽 칩과 CPU가 한 전력을 나눠 쓰고, 그래서 손 추적이 굶어요(중앙값 6장/초까지 떨어졌어요). Low 는 풀·꽃·반딧불이를 줄이는 것 말고도 이렇게 가볍게 해요: ① 그림을 창 크기의 0.7배(가로세로, 점은 약 절반)로 그려서 늘려 보여 줘요(`renderScale`, 늘리는 방식은 Linear). ② 빛 번짐(Bloom)을 더 싼 방식(Dual, 1/4 크기에서 시작, 4단계)으로 해요. ③ 하늘의 구름 두 층을 점마다 계산하지 않고, 앱이 켜질 때 한 번 구워 둔 구름 무늬 판(1024×1024)에서 읽어요(`skyDetail` 0). **배경은 이 단계에서도 움직여요**: 구름이 흐르고, 들판과 먼 그림 위로 구름 그림자와 돌풍 일렁임이 지나가요(모양은 같고 구름 결만 조금 부드러워요). 손이 지나가는 바람은 풀을 가는 쪽으로 기울이기만 하고(되돌아 흔들림 없음), 풀끝 테두리 빛은 빼요. ④ 앱을 초당 30장으로 묶어요(`targetFps` 30: 쉬는 만큼 CPU와 손 추적이 전력을 받아요). MSAA 는 Low 가 원래 꺼져 있어요(1배). `-MaxFps N`(10~240)으로 상한을, 앱을 직접 켤 때 `--render-scale X`(0.3~1)로 그림 크기를 다시 빌드 없이 바꿔 볼 수 있어요. 값은 `content.json` 의 `meadow.quality` 에 있어요. **Mac(Apple M4 Pro, 1920×1080 창, 상한 없이 달린 프레임 시간, 3번 평균, `measurements/day5/render-levers-mac.json`):** Low 기본 1.23 ms 에서 그림 크기 0.85배 −10%, 0.7배 −19%, 0.6배 −22%; 구름·일렁임을 아예 빼면 −6% 였지만(이 측정은 위 파일이고, 배경이 정적이라 심심해서 이제는 빼지 않아요); 빛 번짐 끄기 −5%(싼 방식으로 바꾸기만 하면 변화 없음); MSAA 2배로 올리면 +10%; 0.7배 + 싼 번짐 + 구름 빼기를 합치면 −25%(0.93 ms; 구름을 빼던 때의 값). 이 Mac의 그래픽은 훨씬 빠르고 프레임의 큰 몫이 CPU 쪽이라 절약 폭이 작게 나와요. **시각 다듬기(Part D)와 움직이는 배경의 값**(같은 Mac, 1920×1080 창, 상한 없이, 3번 평균씩 4번을 번갈아 12번 평균, `measurements/day5/render-cost-partd-final.json`): Low 프레임 시간은 다듬기 전 0.980 ms, 아홉 가지를 한 뒤 0.998 ms(+1.9%), 약한 등급에서도 배경이 움직이게 한 뒤 1.029 ms(다듬기 전보다 +5.0%, 아홉 가지 뒤보다 +3.0%)예요. 평균의 오차가 1~2%라서 이 숫자는 ±2% 정도로 읽으세요. 둘을 따로 재면 구름을 구운 판에서 읽는 값은 약 +1.5~2%, 먼 들판 일렁임·그림자는 +3~8%로 흔들렸고(재는 값이 한 번에 ±5% 흔들려요), 같은 그림을 내는 더 싼 계산(방향 벡터 정규화 빼기, 구름 색 계산을 프레임에 한 번으로)을 넣어도 이 Mac 에서는 눈에 띄게 줄지 않았어요. 구름이 흐르는지는 Low 창(0.7배)에서 해를 고정하고(`--sky 0.5`) 14초를 녹화해 잰 값으로 확인했어요: 하늘 띠는 이웃한 장면끼리 평균 0.26단계(255 중)로 거의 안 변해 깜빡임이 없고, 4초 간격이면 1.9단계, 12초 간격이면 3.2단계로 꾸준히 달라져요(`measurements/visual/partd-low-window-a.jpg`, `-b.jpg`). Intel UHD 에서 얼마나 될지는 Windows에서 재 봐야 해요(아래 "Windows에서 잴 것"). Low 에서 눈에 보이는 것: 풀 가장자리가 조금 흐리고 점선처럼 보이고(0.7배와 MSAA 없음), 구름 결이 조금 부드러워요(구름은 흘러요). 번짐이 넘치는 일은 없었어요(1920×1080 창 사진 한 쌍: `measurements/visual/low-before-levers.jpg`, `low-after-levers.jpg`). 감시 프로그램은 손 추적을 **보통 위 우선순위(AboveNormal)**로 켜서(관리자 권한 없이) 바쁜 앱에 밀리지 않게 하고, `play-with-hands.ps1` 도 켠 뒤 같은 우선순위를 줘요(못 주면 경고만 하고 그대로 써요). **Windows에서 잴 것:** Low 로 켜서 ① 1분마다 상태 CSV의 `fps_avg`, `tracker_fps`, 늦은 음 수; ② 작업 관리자에서 그래픽 사용률(내장 그래픽이 얼마나 도는지); ③ `-MaxFps 24`, `-MaxFps 45` 와 `--render-scale 0.6/0.85` 를 바꿔 가며 손 추적 장수가 어떻게 달라지는지.

**손 모양 값 맞추기.** 편 손이 `Unknown` 으로 읽히거나 주먹이 잘 안 읽히면 `play-with-hands.ps1 -LogPose`(감시 프로그램이면 `run-supervised.ps1 -LogPose`)로 켜고 힘을 뺀 손, 주먹, 반쯤 편 손을 몇 초씩 들어 보세요. 앱 로그 `Player.log`(`%USERPROFILE%\AppData\LocalLow\ovthemoon\Downbeat\`)에 1초마다 손마다 `[pose]` 한 줄(손가락 비율 4개, 모양, 연주 여부, 칸)이 남으니 그 줄을 보내 주세요(`docs/conducting-play.md` 8절). 옵션을 빼면 아무것도 남기지 않아요.

**콘텐츠(설정, 소리, 그림)는 어디서 읽나요.** 앱은 켤 때 세 곳을 이 순서로 봐요. ① `Downbeat.exe` 옆의 `content` 폴더(손으로 고치는 곳). ② ①이 없거나 쓸 수 없으면, 지난번에 잘 켜졌던 내용을 앱이 따로 복사해 둔 사본(내 계정의 앱 데이터 폴더, `last-known-good`). ③ 그것도 없으면(새 PC) 앱 안에 든 기본 내용. 폴더를 쓸 수 있는지는 `content.json` 이 읽히는지, 숫자가 범위 안인지, 이름(id)이 겹치지 않는지, 소리·그림 파일이 모두 있고 중간에 끊기지 않았는지로 보고, 소리 파일은 Unity가 실제로 열어 보기까지 해요. **고장 난 파일이 하나라도 있으면 그 폴더 전체를 쓰지 않고 다음 곳으로 가요.** 그래서 어느 한 곳이 망가져도 앱은 꺼지지 않아요. 앱 안의 기본 내용까지 망가졌을 때만 못 켜요(그때는 zip을 다시 풀어 주세요). 사본은 앱이 소리까지 다 읽고 준비된 내용만 남겨요. 어느 곳을 썼는지와 이유는 `logs\content-source.txt` 에 남아요. **예전 형식(바람·구름 설정이 생기기 전)의 `content` 폴더를 새 `Downbeat.exe` 옆에 두면 숫자 검사에서 거절돼요.** 그러면 앱은 앱 안의 기본 내용으로 켜지고, 이유가 `logs\content-source.txt` 에 `meadow.breeze is missing` 같은 줄로 남아요. 새 zip 의 `content` 폴더로 바꿔 주세요. **한 zip 의 `content` 폴더와 다른 zip 의 `Downbeat.exe` 를 섞지 마세요.** 새 앱은 예전 `content` 도 읽지만(없는 값은 앱이 기본값을 써요), 옛 앱은 새 `content` 를 거절해요(`meadow.overlay is missing`: 들판 구석 글을 뺀 `content` 에는 그 항목이 없어요). 실행 중에 `content` 폴더를 고쳤다면 **F5** 를 눌러요. 내용이 멀쩡하면 들판 설정(`meadow`)은 다음 마디부터 바뀌고(들판을 다시 지으니 이미 핀 꽃은 봉오리로 돌아가요. 아직 안 들린 음의 꽃과 입자는 그대로 이어져요), 그 밖(소리, 곡, 손, 화면)은 다음에 켤 때 바뀌어요. 망가졌으면 화면은 그대로이고 이유만 앱 로그의 `[content]` 줄에 남아요.

**기록이 쌓이는 곳** (앱 폴더 = `Downbeat.exe` 가 있는 폴더)

| 파일 | 내용 |
|---|---|
| `logs\supervisor-날짜.csv` | 감시 프로그램의 기록: `시각(UTC),대상,일어난 일,자세한 내용`. 시작·꺼짐·다시 켬·쉼이 모두 여기 있어요. 시각은 UTC라서 한국 시각보다 9시간 늦게 적혀요. |
| `logs\health-날짜.csv` | 앱이 1분마다 쓰는 상태(초당 장면 수, 가장 긴 멈춤, 메모리, 소리 예약 수, 오류 수, 들판 상태, 그 뒤 칸 `audio_state` 는 `starting`/`ok`/`stalled`/`failed`, 맨 끝 네 칸 `render_scale`(그림 크기 비율, 1 = 창의 점 전부), `quality_level`(자동 화질 사다리의 단계, 0 = 가장 가벼움 쪽, **-1 = 화질을 고정했어요**), `starve_ignored`(손 추적의 굶음을 무시 중이면 1), `quality_changes`(그 1분에 자동 화질이 화질을 바꾼 횟수. 등급 바꿈은 풀을 다시 짓느라 프레임 하나가 길어요: 8시간 분석에서 앱이 멈춘 프레임과 가르는 데 써요)). 소리 장치가 없어서 들판이 한 번도 돌지 못한 밤에도 줄이 쌓여요(`experience_state` 가 `Loading`/`Failed`, `fps_avg` 가 0). **`fps_avg`·`fps_min_1s` 는 앱이 그리는 장면 하나의 값이에요:** 들판 화면과 기록 화면은 한 앱이 한 번에 그려서 화면별로 따로 재지 않아요(스펙의 "화면별 fps"는 이 한 값으로 대신해요). 화면 2대로 켜도 값은 한 줄이에요. `tracker_age_ms_p95` 는 손 좌표 한 묶음이 앱의 소켓에 도착한 뒤 장면 계산에 쓰이기까지 기다린 시간의 95번째 값(ms)이에요. 카메라에서 손 추적이 좌표를 보내기까지의 시간은 서로 다른 시계라 여기 들어 있지 않고, 녹화 손을 재생할 때는 비어 있어요. |
| `logs\stalls-날짜.csv` | 0.1초 넘게 멈춘 순간의 목록 |
| `logs\heartbeat.txt` | 마지막으로 살아 있던 시각, `audio=starting`/`ok`/`stalled`/`failed`, 그리고 `app=loading`(켜지는 중)/`running`(떴어요)/`failed`(포기). 옛 앱은 `app=` 줄이 없어요. |
| `tracker\logs\tracker-heartbeat.txt` | 손 추적이 마지막으로 살아 있던 시각과 `camera=starting`(손 인식 모델을 읽는 중: 처음 켜면 내려받아요)/`ok`(사진을 읽고 있어요)/`lost`(카메라가 없어서 다시 여는 중)/`model_failed`(모델을 못 쓰고 기다리는 중) |
| `logs\content-source.txt` | 앱이 켤 때 어느 콘텐츠를 썼는지(`origin=External`/`LastKnownGood`/`BuiltIn`)와, 더 나은 곳을 못 쓴 이유 |
| `logs\exit-reasons.txt` | 앱이 스스로 끈 때마다 한 줄씩 쌓여요(없으면 한 번도 스스로 끈 적이 없어요): `시각(UTC) reason=graphics_device_lost`(로그 갈고리가 장치를 잃었다는 줄을 받음, 종료 코드 71) 또는 `main_thread_stalled`(메인 스레드가 20초 넘게 멈춤, 종료 코드 72), `exit_code=`, `uptime_s=`(켠 지 몇 초), `detail=`(받은 줄이나 멈춘 시간). 71 이 한 번도 없고 72 만 있으면 갈고리가 `d3d11:` 줄을 못 받는 거예요. |
| `tracker\logs` | 손 추적의 기록(초당 처리 장수, 오류) |

**`logs` 폴더를 만들 수 없을 때.** 앱이 `Downbeat.exe` 옆에 `logs` 폴더를 만들 수 없으면(읽기 전용 위치, zip 안, 쓰기가 막힌 폴더에서 실행) 기록을 `C:\Users\(내 이름)\AppData\LocalLow\ovthemoon\Downbeat\logs` 에 대신 쓰는데, 감시 프로그램은 `Downbeat.exe` 옆 `logs` 만 읽어서 "살아 있어요" 파일을 못 찾고 앱을 계속 껐다 켜요. 이때 앱은 `Player.log` 에 오류로 크게 적어요: `[health] the heartbeat and the health log cannot be written in …`. 그 줄이 보이면 앱 폴더를 바탕화면 같은 쓸 수 있는 곳으로 옮겨서 다시 켜요.

## 세션 A: 설치와 첫 실행 (약 40분)

1. 새로 받은 `downbeat-windows-날짜.zip` 을 **`C:\Downbeat`** 에 풀어요(탐색기에서 `C:\` 안에 새 폴더를 만들어요). 바탕화면과 문서 폴더는 OneDrive가 동기화해서, 새로 푼 파일을 올리느라 CPU를 쓰고 손 추적이 느려져요(느렸던 때 OneDrive 0.8코어, 탐색기 1.6코어). 인터넷으로 받았다면 zip을 오른쪽 클릭 → 속성 → **차단 해제** 를 먼저 해요. 아래 명령은 `C:\Downbeat` 를 먼저, 없으면 바탕화면을 찾아요.
2. 손 추적 설치: `README-hand-setup.txt` 의 2절을 따라 한 번 실행해요(5~10분, Y 를 눌러야 시작해요).
3. 감시 프로그램으로 켜요. PowerShell에 붙여 넣고 Enter. 카메라가 없으면 `@('-Mouse')`, 녹화 손이면 `@('-Replay')`, 내장 그래픽 노트북이면 `@('-Backend', 'msmf')` (화질은 앱이 맞춰요).

```powershell
& {
  $extra = @()   # 옵션은 여기에: @('-Camera', '1') / @('-Mouse') / @('-Replay') / @('-Soak') / 내장 그래픽 노트북: @('-Backend', 'msmf') (화질 고정은 '-Quality', 'Low')
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $script = Get-ChildItem $roots -Recurse -Depth 3 -Filter run-supervised.ps1 -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $script) { "C:\Downbeat 와 바탕화면에서 run-supervised.ps1 을 못 찾았어요. 압축을 풀었는지 확인해 주세요."; return }
  "사용할 판: $($script.FullName) (만든 때 $($script.LastWriteTime))"
  powershell -ExecutionPolicy Bypass -File $script.FullName @extra
}
```

4. 앱이 전체 화면으로 열려요. "Windows의 PC 보호" 창이 뜨면 **추가 정보 → 실행**. 끝낼 때는 PowerShell 창에서 **Ctrl+C**(앱과 손 추적도 같이 꺼져요). 창을 못 찾겠으면 아래 명령으로 끝내요.

```powershell
& {
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $script = Get-ChildItem $roots -Recurse -Depth 3 -Filter stop-supervised.ps1 -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $script) { "C:\Downbeat 와 바탕화면에서 stop-supervised.ps1 을 못 찾았어요."; return }
  powershell -ExecutionPolicy Bypass -File $script.FullName
}
```

**끄는 방법은 Ctrl+C 아니면 위 끄기 명령(`stop-supervised`)뿐이에요. PowerShell 창을 X 로 닫아서 끄지 마세요.** X 로 닫으면 감시 프로그램과 손 추적만 꺼지고 앱 화면은 그대로 남아요. 남은 앱이 있는 채로 다시 켜면 앱이 두 개가 되어 소리와 `heartbeat.txt` 가 엉켜요. (다시 켜거나 끄기 명령을 쓰면 이 폴더의 남은 앱은 먼저 정리해 줘요.)

5. **화면 2대 확인** (눈으로): 모니터를 2대 꽂은 상태에서 켜면 한쪽(기본: 1번)에 들판, 다른 쪽에 **오늘의 들판**(꽃 악보, 자연 줄 3개, 카드)이 떠야 해요. 아래 표에 적어 주세요.

| 확인 | 예 / 아니오 | 적어 둘 것 |
|---|---|---|
| 들판이 어느 모니터에 떴나요? (왼쪽/오른쪽, Windows 설정의 1번/2번) | | |
| 기록 화면이 다른 모니터에 떴나요? | | |
| 글자가 2~3 m 떨어져서 읽히나요? | | |
| 손을 움직이면 기록 화면에 꽃이 흘러가나요? | | |

기록 화면이 엉뚱한 모니터에 뜨면 알려 주세요. 앱 폴더의 `content\content.json` 에서 `display.meadowIndex`(들판)와 `display.recordIndex`(기록)를 바꾸면 돼요. 이 문서는 그 파일을 건드리지 않아요. 고친 뒤에는 앱을 다시 켜야 해요(F5 는 들판 설정만 다시 읽어요).

6. **두 화면 한 장 캡처** (손 없이, 소리 없이 25초): 들판과 기록이 나란히 있는 사진 한 장이 `measurements\screens\screens.png` 에 생겨요.

```powershell
& {
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $exe = Get-ChildItem $roots -Recurse -Depth 3 -Filter Downbeat.exe -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $exe) { "C:\Downbeat 와 바탕화면에서 Downbeat.exe를 못 찾았어요."; return }
  $dir = Join-Path $exe.DirectoryName "measurements\screens"
  New-Item -ItemType Directory -Force $dir | Out-Null
  $argLine = "-screen-fullscreen 1 --input replay --mute --capture-screens `"$dir\screens.png`" --capture-after 15 --quit-after 22 -logFile `"$dir\screens.log`""
  Start-Process -FilePath $exe.FullName -ArgumentList $argLine -Wait
  "끝났어요: $dir"
}
```

7. **전체 화면 방식 비교** (각 1분): 테두리 없는 창(borderless)과 독점 전체 화면(exclusive) 중 무엇이 나은지 정하려고 해요. 아래 명령은 둘을 차례로 켜요. 켜져 있는 동안 **Alt+Tab** 을 한 번 눌러 보고, 두 번째 모니터 화면이 깜빡이거나 까매지는지 봐 주세요.

```powershell
& {
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $exe = Get-ChildItem $roots -Recurse -Depth 3 -Filter Downbeat.exe -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $exe) { "C:\Downbeat 와 바탕화면에서 Downbeat.exe를 못 찾았어요."; return }
  $dir = Join-Path $exe.DirectoryName "measurements\fullscreen"
  New-Item -ItemType Directory -Force $dir | Out-Null
  foreach ($kind in "borderless", "exclusive") {
    "실행 중: $kind (약 70초)"
    $argLine = "-screen-fullscreen 1 --fullscreen-mode $kind --input replay --mute --log-frame-stats 20 --quit-after 70 -logFile `"$dir\$kind.log`""
    Start-Process -FilePath $exe.FullName -ArgumentList $argLine -Wait
  }
  "끝났어요: $dir"
}
```

| 방식 | Alt+Tab 하면 | 두 번째 모니터가 깜빡이나요 | 느낌 |
|---|---|---|---|
| borderless | | | |
| exclusive | | | |

## 세션 B: 고장 6가지 일부러 내기 (약 40분)

감시 프로그램(세션 A의 3번)을 켜 둔 채로 하나씩 해요. 하나 하고 나면 앱이 다시 돌아올 때까지(최대 2분) 기다린 다음 다음 것을 해요. 아래 표의 **한 시각** 칸에 시:분:초를 적어 주세요. 감시 기록(`logs\supervisor-날짜.csv`)에서 다시 켜진 시각이 나오니, 복구 시간은 Claude가 계산해요.

| 번호 | 하는 일 | 기대하는 일 | 한 시각 | 화면에서 본 것 |
|---|---|---|---|---|
| B1 | 카메라 USB를 뽑았다가 10초 뒤 다시 꽂아요 | 앱은 마우스로 넘어가고, 카메라가 돌아오면 손으로 돌아와요 | | |
| B2 | 손 추적만 강제로 꺼요 (아래 명령 1) | 3초 뒤 감시 프로그램이 다시 켜요 | | |
| B3 | 앱만 강제로 꺼요 (아래 명령 2) | 3초 뒤 감시 프로그램이 다시 켜요 | | |
| B4 | 설정 파일을 일부러 망가뜨려요 (아래 명령 3), 이어서 앱만 꺼요 (명령 2) | 앱은 3초 뒤 다시 켜지고, 망가진 `content` 대신 **지난번에 잘 켜졌던 사본**으로 켜져요 (세션 A에서 한 번은 정상으로 켜 봤어야 해요). `logs\content-source.txt` 에 `origin=LastKnownGood` 와 이유가 남아요. 실행 중에 F5 를 눌러 보면 화면은 그대로이고 이유만 로그에 남아요 | | |
| B5 | 소리 파일 하나를 치워요 (아래 명령 4), 이어서 앱만 꺼요 (명령 2) | 파일이 하나라도 없으면 `content` 폴더 전체를 쓰지 않고 사본으로 켜요 (소리는 그대로 다 나요). `logs\content-source.txt` 에 빠진 파일 이름이 남아요 | | |
| B6 | 소리 장치를 뽑아요(헤드폰·스피커 USB, 또는 소리 설정에서 기본 장치 끄기) 30초 뒤 다시 꽂아요 | 20초 넘게 소리가 멈추면 감시 프로그램이 앱을 다시 켜요 (`audio_stalled`) | | |

명령 1: 손 추적만 끄기

```powershell
Get-CimInstance Win32_Process -Filter "Name = 'python.exe'" | Where-Object { $_.CommandLine -like '*downbeat_tracker*' } | ForEach-Object { taskkill.exe /PID $_.ProcessId /T /F }
```

명령 2: 앱만 끄기

```powershell
taskkill.exe /IM Downbeat.exe /F
```

명령 3: 설정 파일 망가뜨리기 (먼저 복사본 `content.json.bak` 을 만들어 둬요)

```powershell
& {
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $exe = Get-ChildItem $roots -Recurse -Depth 3 -Filter Downbeat.exe -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $exe) { "C:\Downbeat 와 바탕화면에서 Downbeat.exe를 못 찾았어요."; return }
  $file = Join-Path $exe.DirectoryName "content\content.json"
  Copy-Item $file "$file.bak" -Force
  Set-Content -Path $file -Value "{ 일부러 망가뜨린 파일" -Encoding UTF8
  "망가뜨렸어요. 앱만 끄고(명령 2) 다시 켜지면 logs\content-source.txt 를 본 다음, 바로 아래 명령으로 되돌려 주세요."
}
```

명령 3의 되돌리기 (꼭 해 주세요)

```powershell
& {
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $exe = Get-ChildItem $roots -Recurse -Depth 3 -Filter Downbeat.exe -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $exe) { "C:\Downbeat 와 바탕화면에서 Downbeat.exe를 못 찾았어요."; return }
  $file = Join-Path $exe.DirectoryName "content\content.json"
  if (-not (Test-Path "$file.bak")) { "복사본 content.json.bak 이 없어요. 되돌릴 게 없어요."; return }
  Copy-Item "$file.bak" $file -Force
  "되돌렸어요. 앱이 다시 켜지는지 봐 주세요."
}
```

명령 4: 소리 파일 하나 치우기

```powershell
& {
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $exe = Get-ChildItem $roots -Recurse -Depth 3 -Filter Downbeat.exe -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $exe) { "C:\Downbeat 와 바탕화면에서 Downbeat.exe를 못 찾았어요."; return }
  $file = Join-Path $exe.DirectoryName "content\audio\harp_A4.wav"
  Move-Item $file "$file.moved" -Force
  "치웠어요 (지운 게 아니라 이름만 바꿨어요). 확인한 뒤 바로 아래 명령으로 되돌려 주세요."
}
```

명령 4의 되돌리기 (꼭 해 주세요)

```powershell
& {
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $exe = Get-ChildItem $roots -Recurse -Depth 3 -Filter Downbeat.exe -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $exe) { "C:\Downbeat 와 바탕화면에서 Downbeat.exe를 못 찾았어요."; return }
  $file = Join-Path $exe.DirectoryName "content\audio\harp_A4.wav"
  if (-not (Test-Path "$file.moved")) { "치워 둔 파일이 없어요. 되돌릴 게 없어요."; return }
  Move-Item "$file.moved" $file -Force
  "되돌렸어요."
}
```

## 세션 C: 8시간 구동 (밤)

**이 8시간은 "전시 리허설"이에요: 감시 프로그램이 같이 돌아요.** 앱이나 손 추적이 죽거나 얼면 감시 프로그램이 다시 켜기 때문에, 밤새 켜져 있었다는 사실만으로는 앱이 한 번도 안 죽었다는 뜻이 아니에요. 몇 번 다시 켜졌는지는 `logs\supervisor-날짜.csv` 의 `kill`/`exit`/`start` 줄을 세어 봐요. **"앱 혼자 8시간 버텼다"는 숫자가 필요하면** 감시 프로그램 없이 앱만 켜는 `README-windows-check.txt` 의 5절(밤샘 구동)을 해요. 어느 것을 할지: 앱 자체의 안정성 숫자(서류용) → 점검 문서 5절. 전시와 똑같이 켜 두고 고장 났을 때 스스로 살아나는지 보는 리허설 → 이 세션 C. 둘 다 할 수 있으면 5절을 먼저 해요.

두 가지 중 하나로 해요. 되도록 **방법 1**이 좋아요. 방법 1은 카메라 → 손 추적 → 앱의 전체 길을 시험해요.

- **방법 1: 휴대폰에서 손 영상을 반복 재생해 웹캠 앞에 세워 둬요.** 영상은 손이 계속 움직이는 짧은 것(1분 안쪽)을 반복 재생으로 틀어요. 휴대폰은 충전기를 꽂고 화면이 꺼지지 않게 해 두세요(휴대폰 설정이에요). 그리고 세션 A의 3번 명령을 실행해요(내장 그래픽 노트북이면 `$extra` 에 위의 노트북 옵션만 적어요).
- **방법 2: 카메라 없이 녹화된 손으로.** 3번 명령의 `$extra` 를 `@('-Soak')` 로 바꿔요. 1분 35초마다 손이 와서 연주하고 떠나는 한 바퀴가 돌고, 소리는 나지 않아요. 서류에는 "콘텐츠 앱 8시간 구동(손 추적 경로 제외)"이라고 적게 돼요.

시작 전에 확인할 것: 노트북이면 충전기. Windows 업데이트가 한밤중에 PC를 다시 켤 수 있어요(그러면 앱이 꺼진 시각이 기록에 남아요).

**모니터를 끄기 전에:** 모니터는 밝기를 낮추는 것까지는 괜찮아요. 전원 버튼으로 끌 거라면 **스피커나 헤드폰을 꽂아 두세요.** HDMI/DP 로 연결한 모니터가 이 PC의 유일한 소리 출력이면, 그 모니터를 끄는 순간 소리 장치가 사라져서 소리 시계가 멈추고(`audio_stalled`), 감시 프로그램이 앱을 다시 켜요. 장치가 안 돌아오면 쉬는 시간이 600초까지 늘어서 10분에 한 번씩만 시도하지만, 그동안 앱은 소리 없이 꺼져 있거나 멈춰 있어요. 소리가 나오는 모니터라면 끄지 마세요.

로그인하면 저절로 시작하게 하려면(선택): 아래 명령. Y 를 눌러야 만들어요. 바꾸는 것은 시작 프로그램 폴더의 바로가기 1개뿐이에요.

```powershell
& {
  $extra = @()   # 켤 때와 같은 옵션을 여기에: @('-Soak') / @('-Mouse') / @('-Backend', 'msmf') ...
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $script = Get-ChildItem $roots -Recurse -Depth 3 -Filter install-autostart.ps1 -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $script) { "C:\Downbeat 와 바탕화면에서 install-autostart.ps1 을 못 찾았어요."; return }
  powershell -ExecutionPolicy Bypass -File $script.FullName @extra
}
```

아침에: 앱이 아직 켜져 있나요? 켜져 있으면 세션 A의 4번 방법으로 끄고, 그다음 아래 명령으로 결과 zip을 만들어요.

**아침에 하나 더 (스스로 끄는 기능 확인):** `logs\exit-reasons.txt` 가 있는지 봐요. 없으면 앱이 한 번도 스스로 끈 적이 없어요. 있으면 줄마다 `reason=graphics_device_lost`(71) 인지 `main_thread_stalled`(72) 인지 적어 주세요. `Player.log`(`%USERPROFILE%\AppData\LocalLow\ovthemoon\Downbeat\`) 에서 `d3d11:` 로 시작하는 줄을 찾아 그 줄 끝에 `(Filename:` 꼬리가 붙어 있는지도 알려 주세요. 71 이 아니라 72 만 있다면 로그 갈고리가 그 줄을 받지 못한 거예요(20초 감시가 대신 끈 거예요).

## 결과 보내기 (모든 세션 끝에)

```powershell
& {
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $script = Get-ChildItem $roots -Recurse -Depth 3 -Filter collect-results.ps1 -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $script) { "C:\Downbeat 와 바탕화면에서 collect-results.ps1 을 못 찾았어요."; return }
  powershell -ExecutionPolicy Bypass -File $script.FullName
}
```

바탕화면에 `downbeat-results-날짜-시각.zip` 이 생겨요. 안에는 글과 숫자(기록 CSV, 손 추적 기록, 앱 로그, 측정 결과, 이 PC 정보 `env-report.json`)만 있고 카메라 영상은 없어요. 그래픽 카드 이름과 카메라·소리 장치 이름이 들어 있어요. `env-report.json` 에는 앱 폴더의 전체 경로 대신 폴더 이름만 적어요. 다만 **앱과 손 추적의 기록(`logs`, `tracker-logs`)에는 파일 경로가 들어 있을 수 있어서, `C:\Users\내이름\...` 처럼 Windows 계정 이름이 보일 수 있어요.** 보내기 전에 마음에 걸리면 알려 주세요. 이 zip과, 위 표들(시:분 적은 것)을 Claude에게 보내 주세요.

받은 뒤: Claude가 기록으로 복구 시간과 멈춘 순간을 계산하고, 고칠 점을 반영한 새 판을 만들어요. 새 판이 오면 이 순서표를 처음부터 다시 쓸 필요는 없어요. 고친 부분만 다시 해 봐요.

## 세션 D: 지연 측정

손을 움직인 순간부터 소리가 나기까지, 그리고 화면에 반응이 보이기까지 걸리는 시간 재기예요. 휴대폰 슬로모션(초당 240장)으로 손, 화면, 스피커를 한 장면에 찍고 영상을 한 프레임씩 보며 번호를 적어요. 앱은 `play-with-hands.ps1 -LatencyMode`(감시 프로그램이면 `run-supervised.ps1 -LatencyMode`)로 켜면 화면 오른쪽 위에 하얀 네모가 음마다 반짝여요. 순서는 같은 폴더의 `README-latency.txt` 에 있어요(30번 이상 쳐요). 적은 표(`marks.csv`)를 Claude에게 보내 주면 숫자로 바꿔 줘요.

## 세션 E: 곡 모드 확인 (약 15분, 진짜 손)

곡 모드는 2026-11-01에 생겼어요(설명은 `docs/conducting-play.md` 9절). 방문마다 곡이 하나 흐르고, 위 줄 칸(하프/리코더/종)에 손을 두면 그 곡의 가락이 나와요. Mac에서는 녹화된 손으로만 확인했어요. 이 세션은 **Windows 노트북에서 진짜 손으로** 보는 거예요. 곡 모드가 든 새 판에서, 세션 A의 3번 명령으로 켜요(`@('-Mouse')`나 `@('-Replay')`는 쓰지 않아요. 내장 그래픽 노트북이면 `@('-Backend', 'msmf')`).

**Tab 설정 패널 여는 법**
1. 앱 화면이 맨 앞에 있는 상태에서 키보드 **Tab** 을 한 번 눌러요.
2. 화면 위쪽 가운데에 작은 패널이 열려요. 첫 줄은 "곡 모드 켜짐"(또는 "곡 모드 꺼짐")이에요. (곡이 없는 실행, 예를 들어 `--latency-mode`로 켠 경우에는 "곡 모드: 곡 없음"이라고만 적히고 눌러도 아무 일도 없는 게 맞아요.) 모니터가 1대일 때는 둘째 줄 "기록 띠 보임"(또는 "기록 띠 숨김")도 있어요. 모니터가 2대이면 둘째 줄은 없는 게 맞아요.
3. 줄을 **마우스로** 눌러요. 누를 때마다 켜짐과 꺼짐이 바뀌고 스위치 그림이 초록(켜짐)과 회색(꺼짐)으로 바뀌어요. 줄에 적힌 말은 지금 상태예요. 누르면 할 일이 아니에요.
4. Tab 을 한 번 더 누르거나 10초 동안 아무것도 안 하면 닫혀요. 패널은 손으로는 못 눌러요. 패널 위를 클릭해도 음은 나지 않아요.
5. 패널은 주 모니터(Unity의 0번 화면)에 열려요. 기본 설정에서는 들판이 거기 떠요. 들판을 다른 모니터에 두었는데 패널이 들판 쪽에 안 보이면 알려 주세요.
6. 바꾼 값은 **다음 방문부터** 적용돼요. 치던 곡은 끝까지 가요.

**곡 순서**: 잔잔한 들판 → 밝은 아침 → 올드 랭 사인 → 다시 처음. 앱을 켜면 첫 곡부터예요. 곡은 약 60초이고, 음을 3개 이상 쳐서 카드가 생긴 방문이 끝나야 다음 곡으로 넘어가요.

| 번호 | 하는 일 | 기대하는 일 | 예 / 아니오, 본 것 |
|---|---|---|---|
| E1 | 켠 직후 `Player.log`(`C:\Users\(내 이름)\AppData\LocalLow\ovthemoon\Downbeat\`)를 메모장으로 열어 `[songs]` 를 찾아요 | `[songs] book: 3 songs, 0 rejected` 가 있고 `left out` 줄이 없어요 | |
| E2 | 손을 펴서 위 줄 칸(하프/리코더/종 중 하나)에 두고 천천히 흔들어요 | 첫 곡 "잔잔한 들판"의 가락이 들려요. 가만히 들면 작게, 휘저으면 크게 들려요 | |
| E3 | 방문이 시작된 뒤 화면 위쪽 가운데를 봐요 | 곡 이름 "잔잔한 들판"이 1마디(방문이 시작되고 최대 2.5초 뒤)에 맞춰 3초쯤 떴다 사라져요. 2~3 m 떨어져서도 읽혀요 | |
| E4 | 손을 위 줄에서 아래 줄 칸(낮은 현/마림바/손북)으로 옮겨요 | 가락이 멈추고 반주만 나요. 위 줄에 손이 없으면 가락은 안 들려요(맞는 동작이에요) | |
| E5 | 곡 하나가 끝날 때까지(약 60초) 손을 들고 있어요 | 마지막 마디가 울리고 화면 가운데에 "끝"이 떠요. 기록 화면에 카드가 **한 장** 생기고 카드에 곡 이름 한 줄이 있어요. `Player.log` 에 `[songs] end quiet-meadow` | |
| E6 | 손을 내렸다가 다시 펴서 새 방문을 열어요(끝난 뒤 1.5초가 지나고 편 손이 보여야 열려요) | 이번에는 "밝은 아침"이 나와요. 한 번 더 하면 "올드 랭 사인", 그다음은 다시 "잔잔한 들판" | |
| E7 | 음을 몇 개 치고(3개 이상) 곡이 끝나기 전에 손을 화면 밖으로 빼요 | 소리가 잦아들고 카드가 생겨요. 카드에 그때 치던 곡 이름이 있어요. 다음 방문은 **다음 곡의 처음부터**예요 | |
| E8 | 손을 잠깐 올렸다가 음이 3개 안 되게 빼요(지나가던 사람) | 카드가 안 생기고, 다음 방문은 **같은 곡의 처음부터**예요 | |
| E9 | Tab 으로 패널을 열고 "곡 모드 꺼짐"으로 바꿔요. 치던 방문은 그대로 이어가요 | 치던 곡은 끝까지 가요. **다음 방문**부터 자유 연주예요: 곡 이름이 안 뜨고, 위 줄은 예전처럼 짧은 악구를 되풀이하고, 카드에 곡 이름 줄이 없어요 | |
| E10 | 패널을 열고 "곡 모드 켜짐"으로 되돌려요 | 다음 방문이 곡 모드예요 (끄기 전에 들었던 곡의 다음 곡. 꺼 둔 동안의 방문은 순서를 넘기지 않아요) | |
| E11 | (모니터 1대일 때만) 패널의 둘째 줄을 눌러 "기록 띠 숨김"으로 바꿔요 | 아래 기록 띠가 사라져요. 다시 누르면 "기록 띠 보임"이고 띠가 돌아와요. 예전에 Tab 이 하던 일이에요 | |
| E12 | "곡 모드 꺼짐"으로 두고 앱을 끈 뒤(세션 A의 4번) 다시 켜요. Tab 으로 패널을 열어 봐요 | "곡 모드 꺼짐"으로 시작해요. 방문은 자유 연주예요. 감시 프로그램이 앱을 다시 켰을 때(세션 B의 B3)도 같아요 | |
| E13 | 시험이 끝나면 "곡 모드 켜짐"으로 되돌려 둬요 | 같은 폴더의 `settings.json` 에 `{"songMode":true}` 가 적혀요 | |

- 고른 값은 앱 폴더가 아니라 `C:\Users\(내 이름)\AppData\LocalLow\ovthemoon\Downbeat\settings.json` 에 저장돼요(카드가 쌓이는 `cards` 폴더 옆). 파일을 지우면 콘텐츠 기본값(켜짐)으로 돌아가요.
- 손 추적이 느린 때(`tracker-logs` 의 처리 속도가 1초에 6장쯤)에도 가락이 박자에서 어긋나지 않고 악보의 칸에서 나는지 한 번 봐 주세요(E2). 어긋나면 어느 때인지 알려 주세요.
- 곡이 없는 경우는 맞는 동작이에요: 세션 D의 `-LatencyMode`(지연 측정 중에 60초 곡 끝이 소리를 끄면 안 돼서), `-PlayMode strike`(내려치기).
- 결과 zip(`collect-results`)에는 `Player.log` 가 안 들어갈 수 있어요. E1, E5, E12 의 `[songs]` 줄은 메모장으로 `Player.log` 를 열어 `[songs]` 를 찾아 복사해서 표와 함께 보내 주세요. 스위치를 바꾼 줄에는 시각이 있어요: `[songs] switch off (tab) at 2026-11-01 10:42:05`.

## 문제가 생겼을 때

- **쉬는 시간이 점점 길어져요(60, 120, 240 … 600초)**: 감시 기록에 `backoff_escalated` 가 보여요. 가장 흔한 이유는 소리 장치가 없는 거예요(모니터가 유일한 소리 출력인데 꺼져 있을 때). 그 줄의 `last:` 뒤에 마지막 이유(`audio_failed`, `audio_stalled`, `heartbeat_missing`, `exit` …)가 적혀 있어요. 장치를 되살리면 다음 시도(최대 10분 안)에 저절로 살아나요. 급하면 `stop-supervised.ps1` 로 끄고 다시 켜요.
- **같은 오류가 계속 나서 60초씩 쉬어요**: 감시 기록에 `backoff` 가 보여요. 그 줄 위의 `exit` 줄과 `code=` 숫자를 Claude에게 보내 주세요.
- **앱이 저절로 꺼졌다 켜졌어요 (감시 기록에 `exit` 와 `code=71` 또는 `code=72`)**: 71 은 앱의 로그 갈고리가 그래픽 장치를 잃었다는 줄을 받은 거예요. 72 는 앱이 20초 넘게 장면을 그리지 못해서 20초 감시가 끈 거예요(그래픽 장치를 잃은 경우도 갈고리가 그 줄을 못 받으면 72 로 나와요). 드라이버가 시간 초과로 다시 시작된 경우가 많아요(Windows 오류 보고에 `LiveKernelEvent` 141 이 남기도 해요). 이유와 시각은 `logs\exit-reasons.txt` 와 `Player.log` 의 `[selfexit]` 줄에 있어요. 하룻밤에 한두 번이면 드라이버 문제일 가능성이 커요(새 드라이버로 다시 해 보세요: 설치는 사용자가 정해요). 자주 나면 그 줄과 `logs\health-날짜.csv` 를 Claude에게 보내 주세요. 끄려면 앱 옵션에 `--stall-exit-sec 0`.
- **감시 프로그램 창이 안 보여요**: 시작 프로그램으로 켜면 작은 창으로 숨어 있어요. 작업 표시줄 아래쪽에서 찾거나, 위의 끄기 명령을 써요. 그 작은 창도 X 로 닫지 마세요(앱이 남아요).
- **카메라가 안 열려요**: `README-hand-setup.txt` 5절.
- **전부 지우기**: 자동 시작은 `uninstall-autostart.ps1`(Y 를 눌러야 지워요. `-StopNow` 를 붙이면 켜져 있는 것도 꺼요), 손 추적 설치는 `remove-hand-tracker.ps1` 이에요.
