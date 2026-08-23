# 测试素材获取速查（Agent 用 - 中国大陆实机专用）

**何时查本文件**：开发中任何时刻需要“图标 / 图片 / 音频 / 视频 / 字体 / 测试数据 / 动画 / 3D 模型 / 损坏文件 / 边界文件”时，先到这里找答案，再决定生成还是下载。

> **环境约束**：Windows 目标实机默认只有 **git + curl.exe + PowerShell 5.1**（Windows 自带，**无 Python / node.js / ffmpeg**）。
> **网络环境**：所有外部资源必须在中国大陆网络直连畅通（全部使用国内 CDN、国内镜像、阿里云 OSS、清华 TUNA、Gitee 及 npmmirror），严禁依赖海外未镜像站点。
> **实机验证状态**：所有保留条目均已于 2026-08-23 经 Windows 实机逐一验证（HTTP 200 / 文件头合法 / 仓库克隆成功）。

---

## 第一条原则：先判断“生成还是下载”

```text
能本地生成/系统自带 → 本地直接取用（零网络，最快最稳，第一优先级）
需要特定外部真实内容 → 走国内镜像与直链下载（第二优先级）
```

1. **短音效 / 提示音 / 闹钟铃声**：**100% 优先使用 Windows 自带音频（`C:\Windows\Media\`）**，零网络、零下载。
2. **UI 控件图标与头像**：**100% 优先使用 XAML 原生控件与系统自带字体**（`Segoe Fluent Icons` 与 `PersonPicture` 控件），无文件 I/O 开销。
3. **Zip 压缩包与测试数据**：使用 PowerShell 原生 `Compress-Archive` 与 `Set-Content` 本地秒级生成。
4. **真实背景图片**：使用 `img.scdn.io`（国内 CDN，仅用稳定 `tag=风景` 或纯随机）。

---

## 一、 本地生成与原生文件（PowerShell 5.1 原生，零网络）

```powershell
# 1. 纯色 / 渐变占位 PNG（System.Drawing）
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 800,600
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(255, 0, 120, 215)) # Fluent 蓝
$bmp.Save("$PWD\Assets\placeholder.png", [System.Drawing.Imaging.ImageFormat]::Png)

# 2. 文本 / CSV / JSON 测试数据
Set-Content -Path "$PWD\testdata\data.csv" -Value "id,name,role`n1,张三,管理员`n2,李四,用户" -Encoding UTF8

# 3. 标准 Zip 压缩包（原生 PowerShell，无需外部工具）
Compress-Archive -Path "$PWD\testdata\data.csv" -DestinationPath "$PWD\testdata\sample.zip" -Force

# 4. 损坏 / 异常 / 边界文件（压力测试用）
# 从正常文件复制并截断，严禁到网上搜索损坏文件
$bytes = [IO.File]::ReadAllBytes("$PWD\Assets\placeholder.png")
[IO.File]::WriteAllBytes("$PWD\testdata\corrupted.png", $bytes[0..100]) # 截断损坏文件
New-Item -ItemType File -Path "$PWD\testdata\zero_byte.dat" -Force | Out-Null # 0 字节文件
```

---

## 二、 真实图片与用户头像素材

### 1. 随机 / 稳定标签真实图片（img.scdn.io 大陆 CDN）
- **接口**：`https://img.scdn.io/api/random.php`
- **稳定参数**：
  - `?tag=风景`（实测 100% 成功）
  - 不带 tag 纯随机（实测 100% 成功）
- **避坑提示**：**严禁使用 `?tag=自然`（100% 报 404）或 `?tag=建筑`（高概率失败）**。
- **重试机制**：`curl` 必须带 `--retry 3 --retry-delay 1`。

```powershell
# 方式 A：直接 302 下载图片（推荐，稳定风景图）
curl.exe -L --retry 3 --retry-delay 1 "https://img.scdn.io/api/random.php?tag=风景" -o ".\Assets\background.webp"

# 方式 B：纯随机图片
curl.exe -L --retry 3 --retry-delay 1 "https://img.scdn.io/api/random.php" -o ".\Assets\random.webp"
```

### 2. 用户头像 / 角色占位 (Avatar)
- **首选（XAML 原生控件，零下载）**：WinUI 3 原生自带 `PersonPicture` 控件，自动渲染首字母与标准圆底色：
  ```xml
  <PersonPicture DisplayName="张三" Initials="ZS" Width="48" Height="48" />
  ```
- **次选（系统字形）**：`<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE77B;" />`
- **SVG 独立文件**：从 Gitee Lucide 镜像提取 `icons/user.svg` 或 `icons/circle-user.svg`。

---

## 三、 UI 图标与 SVG（系统内置字形 & Gitee 镜像）

### 1. Windows 原生 Segoe Fluent Icons 常用字形（最高优先级，零下载）

在 XAML 中直接声明，Windows 11/10 底层内置：

| 图标用途 | XAML 代码 |
| :--- | :--- |
| **设置** | `<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE713;" />` |
| **用户 / 个人** | `<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE77B;" />` |
| **通知 / 铃铛** | `<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xEA8F;" />` |
| **成功 / 对勾** | `<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE73E;" />` |
| **警告 / 错误** | `<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE783;" />` |
| **搜索** | `<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE721;" />` |
| **添加 / 新建** | `<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE710;" />` |
| **删除 / 垃圾桶** | `<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE74D;" />` |
| **收藏 / 爱心** | `<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xEB51;" />` |
| **商品 / 购物车** | `<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE7BF;" />` |

### 2. 通用矢量图标 Lucide 国内镜像（SVG 独立文件推荐）
- **仓库**：`https://gitee.com/mirrors/lucide.git`

```powershell
git clone --depth 1 https://gitee.com/mirrors/lucide.git ".\TempAssets\lucide"
# 搜索并复制常用生活/主题类图标（cat / music / folder / user 等）
Copy-Item ".\TempAssets\lucide\icons\cat.svg" ".\Assets\cat.svg"
Copy-Item ".\TempAssets\lucide\icons\circle-user.svg" ".\Assets\avatar.svg"
```

### 3. 微软官方 Fluent UI System Icons 国内镜像（特定控件图标备选）
- **仓库**：`https://gitee.com/mirrors/fluentui-system-icons.git`
- **注意**：该镜像包含 11.8 万个小文件，全量克隆耗时较长，仅在确实需要特定微软官方控件图标时使用。

---

## 四、 中文字体（清华大学 TUNA 镜像 - 单文件 OTF）

- **来源**：清华 TUNA 镜像站（思源黑体 Source Han Sans 单文件直接下载，16.5MB，免解压 Zip、免 clone 大仓库）。
- **Regular 常规体**：`https://mirrors.tuna.tsinghua.edu.cn/adobe-fonts/source-han-sans/OTF/SimplifiedChinese/SourceHanSansSC-Regular.otf`
- **Bold 粗体**：`https://mirrors.tuna.tsinghua.edu.cn/adobe-fonts/source-han-sans/OTF/SimplifiedChinese/SourceHanSansSC-Bold.otf`

```powershell
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

## 六、 音频与视频（Windows 原生音效 & 阿里 OSS & Gitee）

### 1. Windows 原生系统音效（最推荐，本地零网络，标准无损 WAV）
Windows 10/11 电脑在 `C:\Windows\Media\` 目录下内置了全套高品质系统音效，开发桌面应用时直接复制使用：

| 音效类型 | 推荐文件路径 |
| :--- | :--- |
| **通知 / 成功** | `C:\Windows\Media\notify.wav` / `chimes.wav` / `tada.wav` |
| **倒计时 / 闹钟** | `C:\Windows\Media\Alarm01.wav`（至 `Alarm10.wav`）/ `Ring01.wav` |
| **按键 / 交互** | `C:\Windows\Media\Windows Navigation Start.wav` / `Windows Pop-up Blocked.wav` |
| **警告 / 错误** | `C:\Windows\Media\chord.wav` / `Windows Foreground.wav` |

```powershell
# 复制原生通知音或闹钟铃声到项目工程
Copy-Item "C:\Windows\Media\notify.wav" ".\Assets\notify.wav"
Copy-Item "C:\Windows\Media\Alarm01.wav" ".\Assets\alarm.wav"
```

### 2. 长语音录音 WAV（阿里云杭州 OSS 官方 ASR 样本，用于语音识别/播放测试）
- **中文语音 WAV**：`https://isv-data.oss-cn-hangzhou.aliyuncs.com/ics/MaaS/ASR/test_audio/asr_example_zh.wav`（177KB，RIFF 合法）
- **日语语音 WAV**：`https://isv-data.oss-cn-hangzhou.aliyuncs.com/ics/MaaS/ASR/test_audio/asr_example_ja.wav`（200KB，RIFF 合法）

```powershell
curl.exe -L --retry 3 --retry-delay 1 "https://isv-data.oss-cn-hangzhou.aliyuncs.com/ics/MaaS/ASR/test_audio/asr_example_zh.wav" -o ".\Assets\sample_speech.wav"
```

### 3. MP4 视频（Gitee 现成测试视频）
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
Copy-Item ".\TempAssets\gltf\2.0\Box\glTF-Binary\Box.glb" ".\Assets\box.glb"
```

---

## 八、 品牌 Logo / Emoji / 假数据（npmmirror 国内镜像）

国内 npm 镜像源：`https://registry.npmmirror.com`

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
UI 控件图标      → Windows 原生 Segoe Fluent Icons 字体字形 (零网络、零下载)
用户头像 (Avatar)→ WinUI 3 原生 PersonPicture 控件 / Lucide user.svg
短音效/通知/闹钟 → Windows 自带 C:\Windows\Media\*.wav (notify.wav / Alarm01.wav)
长语音录音 (WAV) → 阿里云 OSS (asr_example_zh.wav 单文件直下)
独立 SVG 图标    → Gitee Lucide 镜像 (推荐，克隆快) → Gitee Fluent 镜像
真实照片/背景    → img.scdn.io (仅用 ?tag=风景 或 无标签随机图，带 --retry 3)
标准 Zip 压缩包  → PowerShell 原生 Compress-Archive 命令本地生成
纯色/占位 PNG    → PowerShell + System.Drawing 本地生成
中文字体 (OTF)   → 清华大学 TUNA 镜像 (思源黑体单文件直下)
Lottie 动画 JSON → Gitee OpenHarmony Lottie (data.json)
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
   克隆到 `TempAssets\` 的临时仓库提取素材后需及时删除，清理命令使用 `-Recurse -Force` 并捕获异常。
3. **上架前清理**：测试期间使用的临时网络图片（如 `img.scdn.io`），在正式发布提交 Microsoft Store 前必须替换为项目自有原创或明确商业授权资产。