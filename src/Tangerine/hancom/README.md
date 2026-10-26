# 한글 오토메이션 보안 승인 모듈

`FilePathCheckerModuleExample.dll` 은 한컴이 공개한 보안 승인 모듈 예제를 그대로 가져온 것입니다.

- 출처: [hancom-io/devcenter-archive](https://github.com/hancom-io/devcenter-archive) 의 `hwp-automation/보안모듈(Automation).zip`
- 32비트 (한글 2018 의 `hwp.exe` 가 32비트)
- 하는 일: `IsAccessiblePath` 가 늘 `TRUE` 를 돌려준다. 한글이 자동화로 파일을 열거나 저장할 때마다 띄우는
  "한글을 이용하여 위 파일에 접근하려는 시도…" 확인 창을 건너뛰게 한다.

탠저린은 문서를 PDF 로 바꿀 때 `HKCU\Software\HNC\HwpAutomation\Modules` 에 이 DLL 의 위치를 적고
`RegisterModule("FilePathCheckDLL", …)` 로 그 한글 창에서만 쓴다.
