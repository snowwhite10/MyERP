import { defineConfig } from 'vitest/config';

// 由 angular.json 的 test.options.runnerConfig 載入（ADR-020）。
// 整體並行執行時，每個 worker 的第一個渲染 ng-zorro 元件的測試冷啟動常超過預設 5 秒；
// 逾時後 TestBed 未重設，同一 worker 後續測試全部連帶失敗，因此放寬單一測試逾時。
export default defineConfig({
  test: {
    testTimeout: 15_000,
  },
});
