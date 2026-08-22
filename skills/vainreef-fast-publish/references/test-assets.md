# 测试素材获取速查（Agent 用）

**何时查本文件**：开发中任何时刻需要"图标 / 图片 / 音频 / 字体 / 测试数据 / 动画 / 3D 模型 / 损坏文件 / 边界文件"时，先到这里找答案，再决定生成还是下载。SKILL.md 第 4 节（设计阶段）和第 5 节（编码循环）都会引导到这里。

环境约束：**只有 git + curl.exe + PowerShell 5.1**（Windows 自带）。没有 node.js / Python / ffmpeg / ImageMagick，不要依赖它们。若实机额外有 ffmpeg/ImageMagick 可加分，但没有就用下面的方式。

> 本清单所有来源已于 2026-08-22 实测验证可用（HTTP 200 / git HEAD 可达）。

## 第一条原则：先判断"生成还是下载"

```text
能本地生成 → 本地生成（PowerShell + System.Drawing，零网络）
需要"真实内容"（照片/图标/字体/声音）→ 才下载
```

- 测试图片控件：PowerShell 生成纯色/渐变 PNG 就够，不需要照片
- 只有验证"搜索 cat 图片"这类真实功能时，才去 Openverse

## 本地生成（PowerShell，已验证可用）

```powershell
# 纯色/渐变 PNG（System.Drawing）
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 800,600
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::Orange)
$bmp.Save("$PWD\test.png")
# 圆角/渐变/文字也可用 System.Drawing 完成（参考第一轮图标生成经验）

# 文本/CSV/JSON 测试数据
Set-Content -Path "$PWD\data.csv" -Value "id,name`n1,Alice`n2,Bob" -Encoding UTF8
# 注意：PowerShell 5.1 执行含中文的 .ps1 必须 UTF-8 BOM，否则解析错误（commands.md 坑 31）
```

## 直接下载（curl.exe，无 Key 无登录，全部实测 200）

```powershell
# UI 图标 SVG（Iconify，聚合 30 万+ 图标）
# 搜索图标名：curl.exe "https://api.iconify.design/search?query=cat&limit=10"
curl.exe -L "https://api.iconify.design/lucide/cat.svg" -o Assets\cat.svg
curl.exe -L "https://api.iconify.design/fluent/settings-24-regular.svg" -o Assets\settings.svg
curl.exe -L "https://api.iconify.design/simple-icons/github.svg" -o Assets\github.svg
# 常用集：fluent(UI控件) / lucide / mdi / tabler / material-symbols / bootstrap-icons / simple-icons(品牌Logo)
# 注意：fluent 集只有 UI 控件类图标，没有动物/食物/自然等主题图标——主题图标用 lucide / mdi / tabler

# 任意尺寸占位照片（Picsum，seed 固定可重复）
curl.exe -L "https://picsum.photos/seed/winui-test/800/600.jpg" -o Assets\photo.jpg

# 纯占位图（Placehold.co，尺寸/颜色/文字参数）
curl.exe -L "https://placehold.co/320x180/FF7E67/white?text=Avatar" -o Assets\placeholder.svg

# 头像（DiceBear，seed 固定可重复）
curl.exe -L "https://api.dicebear.com/9.x/initials/svg?seed=Alice" -o Assets\avatar.svg

# 真实照片搜索（Openverse，开放许可，匿名可用）
curl.exe -sG "https://api.openverse.org/v1/images/" --data-urlencode "q=cat" --data "page_size=10"
# 从 results[] 挑 license 允许的取 url 下载；Openverse 搜不到再用 Wikimedia Commons：
# curl.exe "https://commons.wikimedia.org/w/api.php?action=query&format=json&list=search&srsearch=cat&srnamespace=6&srlimit=5"

# 假 JSON 数据
curl.exe "https://jsonplaceholder.typicode.com/users" -o testdata\users.json
curl.exe "https://dummyjson.com/products?limit=20" -o testdata\products.json
```

## git clone（固定测试仓库，全部实测可达；大仓库用 sparse-checkout 只取需要的子目录）

```powershell
# Emoji SVG（OpenMoji，CC BY-SA 4.0；color/svg 为彩色 4565 个，sparse 后约 35MB）
git clone --depth 1 --filter=blob:none --sparse https://github.com/hfg-gmuend/openmoji.git
git -C openmoji sparse-checkout set color/svg
# 黑白版用 black/svg

# Microsoft Fluent Icons：优先用 Iconify 的 fluent 集（curl 直取），仓库源码无现成 SVG 文件，不建议 clone
# 需要批量/离线时才 clone（图标源在 packages/svg-icons 之外，需自行构建）：
# git clone --depth 1 https://github.com/microsoft/fluentui-system-icons.git

# Lottie 动画测试文件
git clone --depth 1 https://github.com/LottieFiles/test-files.git

# 3D 模型（Khronos glTF Sample Assets，注意各模型许可不同；仓库很大，clone 慢）
# 建议：用 API 列出模型后用单文件直链下载，或小模型 sparse-checkout（如 Models/Box）
curl.exe "https://api.github.com/repos/KhronosGroup/glTF-Sample-Assets/contents/Models/Box" | Select-Object -First 20
curl.exe -L "https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/main/Models/Box/glTF-Binary/Box.glb" -o Assets\box.glb

# 中文字体（思源黑体）— 官方 Release 直链（已验证 200）
# 简体子集 SourceHanSansSC.zip 约 90MB；其他：07_J(日) 08_K(韩) 09_SC(简) 10_TC(繁) 11_HC(港)
curl.exe -L "https://github.com/adobe-fonts/source-han-sans/releases/download/2.005R/09_SourceHanSansSC.zip" -o fonts\SourceHanSansSC.zip
# 注意：source-han-sans 仓库本身只有构建脚本没有字体成品，字体必须在 Releases 页下载（不要 clone 该仓库）
```

## 决策树（直接照着走）

```text
UI 图标        → Iconify（fluent 优先，UI 控件类）→ 主题图标用 lucide/mdi/tabler → 品牌 Logo 用 simple-icons
任意照片       → Picsum
指定内容照片   → Openverse → Wikimedia Commons
占位图         → Placehold.co
头像           → DiceBear
Emoji          → OpenMoji（git clone）
字体（中文）   → 思源黑体 GitHub
Lottie         → LottieFiles/test-files
3D             → Khronos glTF-Sample-Assets
假 JSON        → JSONPlaceholder / DummyJSON
测试音/视频    → 没有 ffmpeg 时跳过；有则 ffmpeg 生成
图标/占位 PNG  → PowerShell + System.Drawing 本地生成
损坏/边界文件  → 正常文件复制后截断/改头（PowerShell 即可）
```

## 许可红线

- 首选 CC0/Public Domain；OpenMoji 是 CC BY-SA（保留署名）
- Simple Icons 文件开源 ≠ 品牌商标可商用；正式产品 UI 遵循品牌规范
- 测试内部用无所谓，上架前清理所有测试素材

## 素材分类（24 类，按需取用）

App 图标/MSIX 资源、UI 图标、品牌 Logo、位图、SVG、头像、Emoji、插画、纹理背景、色板主题、字体、音效、音乐、视频、动画/Lottie、3D 模型、JSON 假数据、CSV 表格数据、Office/PDF 文档、压缩包、数据库、二维码/条形码、剪贴板/拖拽素材、损坏/边界/压力测试素材。

每个素材再考虑 4 个维度：正常 / 边界 / 损坏 / 大型。异常文件（0 字节、损坏 PNG、截断 MP4、无效 JSON）用 PowerShell 从正常文件复制后截断/改字节生成，不找网站。