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

`store run` 的 deadline 是整轮总预算，不能在每个 phase 重新计时。未知、Processing、Error、重复包和缺少证据都保持未完成。

## 资料

产品文案、截图和 Identity 在用户确认前不得视作已完成；占位文字只能作为模板。最终认证提交由用户亲自复核并点击。
