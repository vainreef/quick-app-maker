# 测试素材获取速查（Agent 用 - 中国大陆实机专用）

**何时查本文件**：开发中任何时刻需要“图标 / 图片 / 音频 / 视频 / 字体 / 测试数据 / 动画 / 3D 模型 / 损坏文件 / 边界文件”时，先到这里找答案，再决定生成还是下载。

> **环境约束**：Windows 目标实机默认只有 **git + curl.exe + PowerShell 5.1**（Windows 自带）。
> **网络环境**：所有外部资源必须在中国大陆网络直连畅通（全部使用国内 CDN、国内镜像、阿里云 OSS、清华 TUNA、Gitee、ModelScope 及 npmmirror），严禁依赖海外未镜像站点。

---

## 第一条原则：先判断“生成还是下载”

```text
能本地生成 → 本地生成（PowerShell + System.Drawing，零网络，最稳定）
需要真实内容（照片/图标/字体/声音/视频/模型）→ 走国内镜像与直链下载
```

1. **测试图片控件/布局**：优先使用 PowerShell 本地生成纯色/渐变 PNG 或直接在 XAML 用带圆角 `Border` + `LinearGradientBrush` / 系统自带 `FontIcon` 占位，零网络依赖。
2. **需要真实照片/分类图片**：使用 `img.scdn.io`（国内 CDN）或 ModelScope 猫狗数据集。
3. **UI 图标**：优先在 XAML 中使用 Windows 自带 `Segoe Fluent Icons` 字体图标；需要独立 SVG 图标时，按顺序：先 Gitee Fluent 镜像，找不到再找 Gitee Lucide 镜像。

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

## 二、 真实图片素材（国内 CDN & 阿里 ModelScope）

### 1. 随机 / 按标签真实图片（img.scdn.io 大陆 CDN）
- **接口**：`https://img.scdn.io/api/random.php`
- **特点**：支持 `tag`（如 `风景`、`自然`、`建筑` 等），默认直接 302 重定向到图片；支持 EdgeOne / ESA 大陆 CDN。
- **注意**：适合临时测试与占位，非统一开源许可，不建议作为最终上架商用素材。

```powershell
# 方式 A：直接 302 下载图片
curl.exe -L "https://img.scdn.io/api/random.php?tag=风景" -o ".\Assets\background.webp"

# 方式 B：先获取 JSON 元数据再下载
$r = Invoke-RestMethod "https://img.scdn.io/api/random.php?tag=风景&format=json"
curl.exe -L $r.data.image_url -o ".\Assets\background.webp"
```

### 2. 猫狗分类真实 JPG 数据集（ModelScope 阿里开源社区）
- **仓库**：`https://www.modelscope.cn/datasets/tany0699/cats_and_dogs.git`
- **说明**：包含 345 张真实猫狗 JPG 图片，约 10.3MB，国内直连极速。

```powershell
git lfs install
git clone https://www.modelscope.cn/datasets/tany0699/cats_and_dogs.git ".\TempAssets\cats-dogs"
# 查找与提取图片
Get-ChildItem ".\TempAssets\cats-dogs" -Recurse -Filter *.jpg
Copy-Item ".\TempAssets\cats-dogs\images\*.jpg" ".\Assets\" -Force
```

---

## 三、 UI 图标与 SVG（Gitee 官方每日同步镜像）

> **图标选型铁律**：
> 1. 首选 XAML 内置 `<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE713;" />`（0 下载，原生矢量）。
> 2. 需要独立 SVG 文件时：**先 Fluent UI Icons 镜像**（最契合 WinUI 3 设计风格），**找不到再查 Lucide 镜像**。

### 1. 微软官方 Fluent UI System Icons 国内镜像
- **仓库**：`https://gitee.com/mirrors/fluentui-system-icons.git`

```powershell
git clone --depth 1 https://gitee.com/mirrors/fluentui-system-icons.git ".\TempAssets\fluent"
# 搜索常用控件图标
Get-ChildItem ".\TempAssets\fluent" -Recurse -Filter "*settings*.svg"
Get-ChildItem ".\TempAssets\fluent" -Recurse -Filter "*search*.svg"
Get-ChildItem ".\TempAssets\fluent" -Recurse -Filter "*camera*.svg"
# 复制所需图标到工程
Copy-Item (Get-ChildItem ".\TempAssets\fluent" -Recurse -Filter "ic_fluent_settings_24_regular.svg" | Select-Object -First 1).FullName ".\Assets\settings.svg"
```

### 2. 通用矢量图标 Lucide 国内镜像
- **仓库**：`https://gitee.com/mirrors/lucide.git`

```powershell
git clone --depth 1 https://gitee.com/mirrors/lucide.git ".\TempAssets\lucide"
# 搜索主题/生活类图标
Get-ChildItem ".\TempAssets\lucide" -Recurse -Filter "*cat*.svg"
Get-ChildItem ".\TempAssets\lucide" -Recurse -Filter "*music*.svg"
Get-ChildItem ".\TempAssets\lucide" -Recurse -Filter "*folder*.svg"
```

---

## 四、 中文字体（清华大学 TUNA 镜像 - 单文件 OTF）

- **来源**：清华 TUNA 镜像站（思源黑体 Source Han Sans 单文件直接下载，免解压、免 clone 大仓库）。
- **Regular 常规体**：`https://mirrors.tuna.tsinghua.edu.cn/adobe-fonts/source-han-sans/OTF/SimplifiedChinese/SourceHanSansSC-Regular.otf`
- **Bold 粗体**：`https://mirrors.tuna.tsinghua.edu.cn/adobe-fonts/source-han-sans/OTF/SimplifiedChinese/SourceHanSansSC-Bold.otf`

```powershell
# 单文件直下到工程 Assets 目录
curl.exe -L "https://mirrors.tuna.tsinghua.edu.cn/adobe-fonts/source-han-sans/OTF/SimplifiedChinese/SourceHanSansSC-Regular.otf" -o ".\Assets\SourceHanSansSC-Regular.otf"
```
> **WinUI 3 XAML 引用规范**：
> 在 XAML 中引用自定义字体需添加 FontFamily 声明：`FontFamily="ms-appx:///Assets/SourceHanSansSC-Regular.otf#Source Han Sans SC"`。

---

## 五、 Lottie 动画 JSON（Gitee 开源项目）

- **仓库**：`https://gitee.com/openharmony-tpc/lottie.git`
- **内置测试动画路径**：`entry/src/main/ets/common/lottie/data.json`

```powershell
git clone --depth 1 https://gitee.com/openharmony-tpc/lottie.git ".\TempAssets\lottie"
Copy-Item ".\TempAssets\lottie\entry\src\main\ets\common\lottie\data.json" ".\Assets\animation.json"
```

---

## 六、 音频与视频（阿里 OSS 官方样本 & Gitee）

### 1. 音频 WAV（阿里云杭州 OSS 官方 ASR 测试语音，单文件直链）
- **中文语音 WAV**：`https://isv-data.oss-cn-hangzhou.aliyuncs.com/ics/MaaS/ASR/test_audio/asr_example_zh.wav`
- **日语语音 WAV**：`https://isv-data.oss-cn-hangzhou.aliyuncs.com/ics/MaaS/ASR/test_audio/asr_example_ja.wav`

```powershell
curl.exe -L "https://isv-data.oss-cn-hangzhou.aliyuncs.com/ics/MaaS/ASR/test_audio/asr_example_zh.wav" -o ".\Assets\sample.wav"
```

### 2. MP4 视频（Gitee 现成测试视频）
- **仓库**：`https://gitee.com/cheng_gitee/BXC_VideoAnalyzer_v4.git`
- **内置测试视频路径**：`data/test.mp4`

```powershell
git clone --depth 1 https://gitee.com/cheng_gitee/BXC_VideoAnalyzer_v4.git ".\TempAssets\video-test"
Copy-Item ".\TempAssets\video-test\data\test.mp4" ".\Assets\sample.mp4"
```

---

## 七、 3D 模型 GLB / glTF（Khronos Sample Models Gitee 镜像）

- **仓库**：`https://gitee.com/marsqiu/glTF-Sample-Models.git`
- **说明**：完整包含 Khronos 官方 3D 测试模型（如 Box、Duck 等），国内克隆速度快。

```powershell
git clone --depth 1 https://gitee.com/marsqiu/glTF-Sample-Models.git ".\TempAssets\gltf"
# 查找标准模型
Get-ChildItem ".\TempAssets\gltf\2.0\Box" -Recurse -Filter *.glb
Copy-Item ".\TempAssets\gltf\2.0\glTF-Binary\Box.glb" ".\Assets\box.glb"
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

## 九、 素材决策树速查表（中国大陆专用）

```text
UI 图标 (首选)   → Windows 原生 Segoe Fluent Icons 字体图标 (无需下载)
UI 图标 (SVG)    → Gitee Fluent 镜像 → 找不到查 Gitee Lucide 镜像
任意/标签真实图   → img.scdn.io 大陆 CDN (风景/自然/建筑)
猫狗/分类数据集   → ModelScope (cats_and_dogs 仓库)
纯色/占位 PNG    → PowerShell + System.Drawing 本地生成
中文字体 (OTF)   → 清华大学 TUNA 镜像 (思源黑体单文件直下)
Lottie 动画 JSON → Gitee OpenHarmony Lottie (data.json)
音频 (WAV)       → 阿里云 OSS (asr_example_zh.wav 单文件直下)
视频 (MP4)       → Gitee BXC_VideoAnalyzer_v4 (data/test.mp4)
3D 模型 (GLB)    → Gitee glTF-Sample-Models (Box.glb 等)
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
2. **临时素材清理**：所有克隆到 `TempAssets\` 的临时仓库在提取完素材后应及时清理，避免污染工程。
3. **上架前清理**：测试期间使用的临时网络图片（如 `img.scdn.io`、ModelScope 样本），在正式发布提交 Microsoft Store 前必须替换为项目自有原创或明确商业授权资产。