# 공명 제약(안개 / 시야 제약) 테스트용 끄기

테스트 중 공명 제약 연출(안개 + Vignette 등)을 인스펙터 옵션으로로 껐다 켤 수 있다.

## 사용법

1. `StartScene`의 `VRSystem/ResonanceSystem` 오브젝트 선택
2. `ResonanceController`의 **Constraint Enabled** 체크 해제 → 제약 연출 OFF
3. 다시 체크 → 원래대로 복귀

플레이 중에도 실시간으로 껐다 켤 수 있다.

## 참고

- **테스트 전용** 디버그 옵션이다. 커밋 전 켜진 상태(true)인지 확인할 것.
- 에디터 플레이 중 인스펙터 토글은 즉시 반영된다.
