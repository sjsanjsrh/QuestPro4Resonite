# 설치방법(세로운 사용자용)
[English](/HowToInstallALXR.md) 
1. 오큘러스 계정을 개발자 계정으로 등록합니다.

2. Quest Pro를 개발자 모드로 변경합니다.

3. adb 또는 sidequst를 사용하여 [alxr-client-quest.apk](https://github.com/korejan/ALXR-nightly/releases/latest/download/alxr-client-quest.apk)를 설치 합니다.[https://github.com/korejan/ALXR-nightly/releases](https://github.com/korejan/ALXR-nightly/releases)
    >이 모드는 [2023.12.24](https://github.com/korejan/ALXR-nightly/releases/tag/v0.21.0%2Bnightly.2023.12.24)버전에서 테스트 되었습니다.

4. PC에서 Oculus 애플리케이션을 열고 설정 -> 일반으로 이동하세요. "알 수 없는 출처"를 켭니다. 현재 "OpenXR Runtime"이 Oculus로 설정되어 있는지 확인하세요.
    > ![image](https://github.com/sjsanjsrh/QuestPro4Resonite/assets/16241081/ff756c0d-5f3b-45ff-a342-8b6867bb4bdf)

5. 설정 -> 베타로 이동하세요. "개발자 런타임 기능"을 켠 다음 "시선 트래킹 오버 Oculus Link"를, "자연스로운 표정 오버 Oculus Link"를 켜세요.
    > ![image](https://github.com/sjsanjsrh/QuestPro4Resonite/assets/16241081/e10c0457-79ba-487a-9ee9-d82b5ac30887)

 6. [alxr-client-win-x64.zip](https://github.com/korejan/ALXR-nightly/releases/latest/download/alxr-client-win-x64.zip)를 다운로드하고 압축을 푸세요. nvida그레픽카드가 아닐경우 [alxr-client-win-x64-no-nvidia.zip](https://github.com/korejan/ALXR-nightly/releases/latest/download/alxr-client-win-x64-no-nvidia.zip)를 다운로드 [https://github.com/korejan/ALXR-nightly/releases](https://github.com/korejan/ALXR-nightly/releases)
    >이 모드는 [2023.12.24](https://github.com/korejan/ALXR-nightly/releases/tag/v0.21.0%2Bnightly.2023.12.24)버전에서 테스트 되었습니다.
 
 7. ``alxr-client.exe``를 마우스 오른쪽 버튼으로 클릭하고 바로 가기를 만듭니다. 바로가기를 편리한 곳으로 자유롭게 이동하세요.

 8.  alxr-client 바로 가기를 마우스 오른쪽 버튼으로 클릭하고 속성을 클릭합니다. 대상: 끝에 ``--no-alvr-server --no-bindings`` 플래그를 추가합니다. ``alxr-client.exe``와 추가된 플래그 사이에 공백이 있어야 합니다.
     > Vulkan에 대한 오류 메시지와 함께 ALXR이 종료되는 경우 다른 그래픽 백엔드를 사용해 보시고, ``-gD3D12``또는 ``-gD3D11``을 추가할 수 있습니다.
