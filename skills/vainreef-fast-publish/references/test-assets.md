# 测试素材获取速查（Agent 用 - 中国大陆实机专用）

**何时查本文件**：开发中任何时刻需要“图标 / 图片 / 音频 / 视频 / 字体 / 测试数据 / 动画 / 3D 模型 / 损坏文件 / 边界文件”时，先到这里找答案，再决定生成还是下载。

> **环境约束**：Windows 目标实机默认只有 **git + curl.exe + PowerShell 5.1**（Windows 自带，**无 Python / node.js / ffmpeg**）。
> **网络环境**：所有外部资源必须在中国大陆网络直连畅通（全部使用国内 CDN、国内镜像、阿里云 OSS、清华 TUNA、Gitee 及 npmmirror），严禁依赖海外未镜像站点。
> **实机验证状态**：所有保留条目均已于 2026-08-23 经 Windows 实机逐一验证（HTTP 200 / 文件头合法 / 仓库克隆成功）。

---

## 第一条原则：先判断“生成还是下载”

```text
能本地生成 → 本地生成（PowerShell + System.Drawing，零网络，最快最稳）
需要真实内容（照片/图标/字体/声音/视频/模型）→ 走国内镜像与直链下载
```

1. **测试图片控件/布局**：优先使用 PowerShell 本地生成纯色/渐变 PNG 或直接在 XAML 用带圆角 `Border` + `LinearGradientBrush` / 系统自带 `FontIcon` 占位，零网络依赖。
2. **UI 图标**：**最高优先级**永远在 XAML 中使用 Windows 10/11 自带的 `Segoe Fluent Icons`（0 文件、0 下载、无 I/O 开销）；需要独立 SVG 文件时再查 Gitee 镜像。
3. **真实照片**：使用 `img.scdn.io`（国内 CDN，仅用稳定标签或随机图）。

---

## 一、 本地生成（PowerShell 5.1 原生，零网络）

```powershell
# 1. 纯色 / 渐变占位 PNG（System.Drawing）
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 800,600
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(255, 0, 120, 215)) # Fluent 蓝
$bmp.Save("$PWD\Assets\placeholder.png", [System.Drawing.Imaging.ImageFormat]::Png)

# 2. 文本 / CSV / JSON 测试数据
# 注意：PowerShell 5.1 执行含中文的 .ps1 必须带 UTF-8 BOM，数据文件写入建议用 UTF8
Set-Content -Path "$PWD\testdata\data.csv" -Value "id,name,role`n1,张三,管理员`n2,李四,用户" -Encoding UTF8

# 3. 损坏 / 异常 / 边界文件（压力测试用）
# 从正常文件复制并截断，严禁到网上搜索损坏文件
$bytes = [IO.File]::ReadAllBytes("$PWD\Assets\placeholder.png")
[IO.File]::WriteAllBytes("$PWD\testdata\corrupted.png", $bytes[0..100]) # 截断损坏文件
New-Item -ItemType File -Path "$PWD\testdata\zero_byte.dat" -Force | Out-Null # 0 字节文件
```

---

## 二、 真实图片素材（img.scdn.io 大陆 CDN）

### 随机 / 稳定标签真实图片
- **接口**：`https://img.scdn.io/api/random.php`
- **稳定参数**：
  - `?tag=风景`（实测 100% 成功）
  - 不带 tag 纯随机（实测 100% 成功）
- **避坑提示**：**严禁使用 `?tag=自然`（100% 报 404）或 `?tag=建筑`（高概率失败）**，后端标签覆盖不完整。
- **重试机制**：由于 CDN 偶发 DNS/连接抖动，`curl` 必须带 `--retry 3 --retry-delay 1`。

```powershell
# 方式 A：直接 302 下载图片（推荐，稳定风景图）
curl.exe -L --retry 3 --retry-delay 1 "https://img.scdn.io/api/random.php?tag=风景" -o ".\Assets\background.webp"

# 方式 B：纯随机图片
curl.exe -L --retry 3 --retry-delay 1 "https://img.scdn.io/api/random.php" -o ".\Assets\random.webp"

# 方式 C：先获取 JSON 元数据再下载
$r = Invoke-RestMethod "https://img.scdn.io/api/random.php?tag=风景&format=json"
curl.exe -L --retry 3 --retry-delay 1 $r.data.image_url -o ".\Assets\background.webp"
```

> **注意**：ModelScope 等公开数据集仓库（如 `cats_and_dogs`）在 Git 树中只有元数据文本、不含实际 JPG（需 Python SDK `MsDataset.load` 动态拉取，当前工具链无 Python），**严禁通过 git clone 尝试获取图片数据集**。

---

## 三、 UI 图标与 SVG（Gitee 官方每日同步镜像）

> **图标选型铁律（避坑指南）**：
> 1. **首选**：XAML 内置 `<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE713;" />`（0 下载、0 文件、0 I/O 开销）。
> 2. **次选（通用/生活类图标）**：优先克隆 **Lucide 镜像**（仓库适中、克隆迅速、SVG 文件齐全）。
> 3. **末选（特定微软官方控件图标）**：**Fluent UI Icons 镜像包含 11.8 万个小文件**，全量克隆与删除目录极其消耗磁盘 I/O（耗时极长），非必要不要全量克隆。

### 1. 通用矢量图标 Lucide 国内镜像（推荐，克隆快）
- **仓库**：`https://gitee.com/mirrors/lucide.git`

```powershell
git clone --depth 1 https://gitee.com/mirrors/lucide.git ".\TempAssets\lucide"
# 搜索并复制常用生活/主题类图标（cat / music / folder 等）
Get-ChildItem ".\TempAssets\lucide" -Recurse -Filter "*cat*.svg"
Copy-Item (Get-ChildItem ".\TempAssets\lucide" -Recurse -Filter "cat.svg" | Select-Object -First 1).FullName ".\Assets\cat.svg"
```

### 2. 微软官方 Fluent UI System Icons 国内镜像（备选）
- **仓库**：`https://gitee.com/mirrors/fluentui-system-icons.git`

```powershell
git clone --depth 1 https://gitee.com/mirrors/fluentui-system-icons.git ".\TempAssets\fluent"
# 搜索并复制特定控件图标
Copy-Item (Get-ChildItem ".\TempAssets\fluent" -Recurse -Filter "ic_fluent_settings_24_regular.svg" | Select-Object -First 1).FullName ".\Assets\settings.svg"
```

---

## 四、 中文字体（清华大学 TUNA 镜像 - 单文件 OTF）

- **来源**：清华 TUNA 镜像站（思源黑体 Source Han Sans 单文件直接下载，16.5MB，免解压 Zip、免 clone 大仓库）。
- **Regular 常规体**：`https://mirrors.tuna.tsinghua.edu.cn/adobe-fonts/source-han-sans/OTF/SimplifiedChinese/SourceHanSansSC-Regular.otf`
- **Bold 粗体**：`https://mirrors.tuna.tsinghua.edu.cn/adobe-fonts/source-han-sans/OTF/SimplifiedChinese/SourceHanSansSC-Bold.otf`

```powershell
# 单文件直下到工程 Assets 目录（实测文件头 OTTO 合法）
curl.exe -L --retry 3 --retry-delay 1 "https://mirrors.tuna.tsinghua.edu.cn/adobe-fonts/source-han-sans/OTF/SimplifiedChinese/SourceHanSansSC-Regular.otf" -o ".\Assets\SourceHanSansSC-Regular.otf"
```
> **WinUI 3 XAML 引用规范**：
> `FontFamily="ms-appx:///Assets/SourceHanSansSC-Regular.otf#Source Han Sans SC"`

---

## 五、 Lottie 动画 JSON（Gitee 开源项目）

- **仓库**：`https://gitee.com/openharmony-tpc/lottie.git`
- **内置测试动画路径**：`entry/src/main/ets/common/lottie/data.json`（实测 1.7MB，合法 Lottie v4.0 JSON）

```powershell
git clone --depth 1 https://gitee.com/openharmony-tpc/lottie.git ".\TempAssets\lottie"
Copy-Item ".\TempAssets\lottie\entry\src\main\ets\common\lottie\data.json" ".\Assets\animation.json"
```

---

## 六、 音频与视频（阿里 OSS 官方样本 & Gitee）

### 1. 音频 WAV（阿里云杭州 OSS 官方 ASR 测试语音，单文件直链）
- **中文语音 WAV**：`https://isv-data.oss-cn-hangzhou.aliyuncs.com/ics/MaaS/ASR/test_audio/asr_example_zh.wav`（177KB，RIFF 合法）
- **日语语音 WAV**：`https://isv-data.oss-cn-hangzhou.aliyuncs.com/ics/MaaS/ASR/test_audio/asr_example_ja.wav`（200KB，RIFF 合法）

```powershell
curl.exe -L --retry 3 --retry-delay 1 "https://isv-data.oss-cn-hangzhou.aliyuncs.com/ics/MaaS/ASR/test_audio/asr_example_zh.wav" -o ".\Assets\sample.wav"
```

### 2. MP4 视频（Gitee 现成测试视频）
- **仓库**：`https://gitee.com/cheng_gitee/BXC_VideoAnalyzer_v4.git`
- **内置测试视频路径**：`data/test.mp4`（3.7MB，ftyp 合法 MP4）

```powershell
git clone --depth 1 https://gitee.com/cheng_gitee/BXC_VideoAnalyzer_v4.git ".\TempAssets\video-test"
Copy-Item ".\TempAssets\video-test\data\test.mp4" ".\Assets\sample.mp4"
```

---

## 七、 3D 模型 GLB / glTF（Khronos Sample Models Gitee 镜像）

- **仓库**：`https://gitee.com/marsqiu/glTF-Sample-Models.git`
- **真实模型路径**：`2.0/Box/glTF-Binary/Box.glb`（1.6KB，标准 glTF v2）

```powershell
git clone --depth 1 https://gitee.com/marsqiu/glTF-Sample-Models.git ".\TempAssets\gltf"
# 复制标准 Box 模型（注意包含 \Box\ 子目录）
Copy-Item ".\TempAssets\gltf\2.0\Box\glTF-Binary\Box.glb" ".\Assets\box.glb"
```

---

## 八、 品牌 Logo / Emoji / 假数据（npmmirror 国内镜像）

国内 npm 镜像源：`https://registry.npmmirror.com`

若开发机环境包含 `npm` 或需要批量提取标准包资源：

```powershell
npm config set registry https://registry.npmmirror.com

# 1. 品牌 Logo 矢量库
npm install simple-icons
# 2. 完整 Emoji 库
npm install openmoji
# 3. 真实假数据生成器（中文用户/地址/商品）
npm install @faker-js/faker
```

---

## 九、 素材决策树速查表（中国大陆实机专用）

```text
UI 图标 (首选)   → Windows 原生 Segoe Fluent Icons 字体图标 (零网络、零下载)
UI 图标 (SVG)    → Gitee Lucide 镜像 (推荐，克隆快) → Gitee Fluent 镜像 (11万小文件慎用)
真实照片/背景     → img.scdn.io (仅用 ?tag=风景 或 无标签随机图，带 --retry 3)
纯色/占位 PNG    → PowerShell + System.Drawing 本地生成
中文字体 (OTF)   → 清华大学 TUNA 镜像 (思源黑体单文件直下)
Lottie 动画 JSON → Gitee OpenHarmony Lottie (data.json)
音频 (WAV)       → 阿里云 OSS (asr_example_zh.wav 单文件直下)
视频 (MP4)       → Gitee BXC_VideoAnalyzer_v4 (data/test.mp4)
3D 模型 (GLB)    → Gitee glTF-Sample-Models (2.0/Box/glTF-Binary/Box.glb)
品牌 Logo / Emoji→ npmmirror (simple-icons / openmoji)
假数据 Mock      → PowerShell 本地写 JSON / npmmirror @faker-js/faker
损坏/异常文件    → PowerShell 从正常文件复制截断 (0 字节 / 改字节头)
```

---

## 十、 版权合规与工程引用红线

1. **WinUI 3 工程引用声明**：
   下载到 `Assets\` 目录的所有素材，必须确保在 `.csproj` 中包含（或被通配包含）：
   ```xml
   <ItemGroup>
     <Content Include="Assets\**" CopyToOutputDirectory="PreserveNewest" />
   </ItemGroup>
   ```
2. **临时目录清理与幂等**：
   克隆到 `TempAssets\` 的临时仓库提取素材后需及时删除，清理命令使用 `-Recurse -Force` 并捕获异常，避免因文件句柄或超时阻塞。
3. **上架前清理**：测试期间使用的临时网络图片（如 `img.scdn.io`），在正式发布提交 Microsoft Store 前必须替换为项目自有原创或明确商业授权资产。