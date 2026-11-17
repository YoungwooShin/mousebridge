# Mac–Windows Bluetooth investigation / Mac–Windows 연결 조사

Updated: 2026-11-17. Windows app0.3.0; Mac app0.3.4. Reported Mac system: macOS26.6.2 (25G83).

## 현재 결론

**이 조합에서 Mac 입력 제어는 동작 확인되지 않았습니다.** Mac Main → Windows Secondary는 서비스 검색에 실패했고, 반대 방향에서는 Windows가 Mac 서비스를 발견하고 인증·암호화 소켓 연결 성공을 보고했지만 Mac의 수신 처리·승인창·입력 제어는 확인되지 않았습니다.

Mac0.3.4 로그는 Bluetooth 권한 허용(authorization3), 컨트롤러 존재·전원 켜짐, 수신 알림 등록 및 서비스 광고 성공을 보여 줍니다. Windows가23:42:10(KST)에 연결 성공을 기록한 시도의 뒤에 수집한 Mac 진단에도,23:41:38(KST)의 광고 기록 이후 새 수신·승인 기록이 없었습니다. 이것은 앱 수준의 관찰이며 패킷 추적으로 OS/드라이버 동작을 증명한 결과는 아닙니다.

다음 조사 대상은 **Mac 수신 알림의 전달·필터·이벤트 루프·앱 처리 경로**입니다. 특정 코드 또는 macOS 결함이 원인이라고 확정하지 않았습니다. 전용 Bluetooth 스레드/RunLoop는 검토할 후보이지, 구현·검증된 해결책이 아닙니다. 같은 권한 설정과 재연결만 반복하도록 안내할 근거는 없습니다.

## Evidence and conclusions

| Observation | Supported conclusion | What it does not prove |
| --- | --- | --- |
| Mac Main0.3.2: paired=YES, openConnection returned0, callback status0 but connected=NO; SDP completion absent | Success return/callback and reported connection state were inconsistent; SDP timed out | An established, authenticated application connection |
| Cached service count12 | Mac had cached records | A fresh query succeeded, or MouseBridge's UUID was found |
| Windows Main fresh search found the Mac; ActualProtection=BluetoothEncryptionWithAuthentication(4); Connected logged | Windows reported finding the service and opening a protected socket | Mac app accepted the connection, showed approval or granted input control |
| Mac0.3.4 authorization3; controller present; powerState1 | Current app reports Bluetooth access allowed and controller powered | All input permissions, or reliable delivery of driver events |
| Mac0.3.4 open-notification registration and SDP publish returned success; channel1, uint8 | Registration/publication calls succeeded | Incoming callback execution, remote traffic delivery or end-to-end operation |
| No new Mac incoming/security/approval/engine-ready markers after the reported Windows attempt | App diagnostics do not show progression through the receiving path | Exactly which OS/framework/filter/application layer lost or ignored the event |

Times in Mac diagnostics are UTC(+0000); Windows excerpts use Korean local time(UTC+09). Compare after conversion. Personal device names and addresses, clipboard contents and raw private logs are intentionally omitted here.

## Work completed

| Version | Change | Verification and limits |
| --- | --- | --- |
| Mac0.3.0 | Native Main/Secondary port, common RFCOMM protocol, input/clipboard implementation | Native build/signature verification later passed; not proof of live Mac control |
| Mac0.3.1 | Full SDP query, address-based callback matching, stale-attempt guards, search/open deadlines and diagnostic copy | Native mocked discovery scenarios passed; real search still timed out |
| Mac0.3.2 | Explicit authenticated baseband request before SDP, controller/phase logging and monotonic deadlines | Native mocks/build passed; user observed success callback with connected=NO |
| Mac0.3.3 | RFCOMM SDP channel explicitly uint8, public browse group, confirmed-state gate before SDP | Apple's real SDP parser reproduced old NSNumber width4 and verified corrected width1; this is a confirmed format correction, not proof of resolving every observed failure |
| Mac0.3.4 | Unfiltered notification registration with application-side channel/direction/generation guards; main-thread callback delivery; bounded approval buffer; foreground approval; current authorization/security diagnostics | Native incoming-path mocks/build/signature verification passed; reported physical test still has no incoming/approval markers |

0.3.4 ignores unrelated/outgoing/stale channels without assigning delegates or closing them. Duplicates do not close the active connection. Pending-approval frames are bounded to128 frames/512KiB and delivered once after engine readiness. Paired-device, authentication, encryption and explicit approval requirements remain; no insecure fallback was added.

Windows retains a saved peer. The existing **Find another secondary PC** action clears it and performs a fresh search. Earlier services=0 attempts cannot all be treated as tests against the intended Mac. Later logs explicitly show the Mac was found.

## Tests actually performed

- Windows native Release build and111 xUnit tests: passed. New Mac receiving code does not establish new Windows GUI/hardware results.
- Mac native arm64/x86_64 compilation, universal packaging, Apple ad-hoc codesign verification: passed. No Developer ID signing/notarization.
- Native C wire tests and mocked discovery/baseband scenarios: passed.
- Actual Apple SDP value-parser regression: passed (old uint32 versus corrected uint8).
- Incoming mocks: channel/direction/generation isolation; duplicate/busy behavior; pre-approval FIFO/once-only delivery; overflow cleanup; background-to-main callback delivery: passed.
- Physical Mac permission dialogs, driver notifications, approval UI, input/clipboard and Mac–Mac interoperability: not established by those tests. Reported Mac–Windows input control remains unsuccessful/unconfirmed.

Latest release CI: https://github.com/YoungwooShin/mousebridge/actions/runs/44103787737 . Earlier evidence and artifact hashes: [TEST-RESULTS.md](TEST-RESULTS.md), [verification](verification/).

## Next investigation / 다음 조사

1. Reproduce on a real Mac and Windows using a single correlated attempt. Existing reports are sufficient to establish the current failure; repeated permission/reinstall trials alone are not a fix.
2. Trace OS/framework event delivery and inspect callback ownership, listener lifetime, run-loop scheduling and application filtering. Do not weaken authentication/encryption to make status appear successful.
3. Evaluate a dedicated Bluetooth RunLoop or another documented notification path only with concrete evidence and regression tests. Neither approach is currently validated as the remedy.
4. Require a real incoming callback, successful approval, protocol control grant, input delivery and clean disconnect before reporting Mac interoperability as working. Verify the original Mac-Main direction separately; Windows-Main success alone would not prove it.
5. Mac–Mac remains untested; do not generalize this report into either universal support or universal incompatibility across all Macs/macOS releases.

No radio-capable Mac is attached to the development workspace. This limits what can be established without hardware investigation. Source/mocked-test success is recorded separately from device success.
