# Partner Center V2

## 阶段

- reserve：名称、ProductId、Package Identity；
- preflight：MSIX、manifest、语言、资产、文案；
- launch/discover：独立 Edge profile、登录、SubmissionId 和实时路由；
- availability/properties/age-ratings/packages/listing/options：每次一个阶段；
- verify：冷导航 Overview，六项均为 Complete。

## 页面操作

使用 Playwright Locator、ARIA 角色、Label 和文本。控件动作后重新 Observe。页面变化后重新定位，不保存 node index。Shadow DOM 使用 open root 自动穿透，closed root 进入集中 CDP fallback。

## 完成链

`PageKind → Observe → Diff → Apply → 冷加载 → Diff=0 → Overview Complete → Converged`

提交认证由用户亲自复核并点击。
