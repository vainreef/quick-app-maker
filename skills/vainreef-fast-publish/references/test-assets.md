# 测试素材来源清单（Agent 开发用）

开发 WinUI 3 应用时可能需要测试素材：图标、占位图、音频、字体等。以下来源按 Agent 可编程下载的便利程度排序，全部为免费/可商用（CC0、免费许可证、明确可商用条款）来源。**优先选 CC0/Public Domain**；使用前确认具体许可条款。

## 图标类（最高频）

| 用途 | 来源 | 获取方式 |
| --- | --- | --- |
| UI 图标（按钮/菜单/工具栏，一站式） | **Iconify API**（聚合数千图标集） | `https://api.iconify.design/<set>/<name>.svg` 直链，脚本可批量拉取 |
| 单个 SVG 图标 | SVGRepo、Tabler Icons、Lucide、Feather、Bootstrap Icons、Remix Icon | GitHub 仓库 clone 或单文件直链 |
| 应用图标多尺寸生成 | favicon.io | 上传 PNG 生成 ICO/多尺寸 PNG |
| Segoe Fluent Icons 码点表 | Microsoft Learn 官方图标表 | webfetch 抓取 |

## 图片 / 占位图

| 用途 | 来源 | 获取方式 |
| --- | --- | --- |
| 任意尺寸占位图 | **picsum.photos**（`https://picsum.photos/800/600` 直接返回图片） | URL 即图片，最方便 |
| 测试照片 | Unsplash（API 可按关键词+尺寸下载）、Pexels、Pixabay | API 或页面下载 |
| 固定尺寸占位 | placeholder.com、dummyimage.com | URL 参数指定尺寸/颜色/文字 |

## SVG 矢量 / 插画

| 用途 | 来源 | 获取方式 |
| --- | --- | --- |
| 空状态/引导页插画 | unDraw、storyset、manypixels | SVG 直链或整包下载 |
| 渐变背景图 | Cool Backgrounds、CSSGradient | 生成后存 PNG |

## 音频

| 用途 | 来源 | 获取方式 |
| --- | --- | --- |
| 音效 SFX（CC0 整包） | **Kenney**（游戏音效包，zip 一次拉全部） | 直接下载 zip |
| 音效/提示音 | Zapsplat、Mixkit、Pixabay Audio、freesound.org（有 API） | 页面下载或 API |
| 背景音乐 | Incompetech（Kevin MacLeod）、Bensound | 页面下载 |
| 纯测试音（正弦波/白噪/静音） | 无（用 ffmpeg 生成） | `ffmpeg -f lavfi -i sine=frequency=440:duration=2 out.wav` |

## 视频（媒体类功能才需要）

| 用途 | 来源 | 获取方式 |
| --- | --- | --- |
| 测试视频 | Pexels Videos、Mixkit、sample-videos.com、W3C test media | 直链下载 |
| 合成测试视频 | 无（用 ffmpeg） | `ffmpeg -f lavfi -i testsrc=duration=5 out.mp4` |

## 字体

| 用途 | 来源 | 获取方式 |
| --- | --- | --- |
| 中文字体（思源黑体/宋体、霞鹜文楷、得意黑） | GitHub Releases 直链 | `https://github.com/adobe-fonts/source-han-sans/releases` 等 |
| 英文字体 | Google Fonts API、Fontsource（npm 包） | API 脚本批量下载 ttf |
| Emoji 图片（PNG/SVG） | Twemoji、OpenMoji、Noto Emoji | GitHub 仓库整包 clone |

## 动画 / Lottie（做动效时）

| 用途 | 来源 | 获取方式 |
| --- | --- | --- |
| Lottie JSON | lottiefiles.com（有搜索 API）、Lordicon | API 或页面下载 |
| GIF 动图 | Giphy（有 API）、Tenor | API |

## 测试数据 / 文件

| 用途 | 来源 | 获取方式 |
| --- | --- | --- |
| 示例 JSON（用户/列表/嵌套） | **dummyjson.com**、jsonplaceholder.typicode.com、randomuser.me | API 直取 |
| 自定义 schema 假数据（CSV/JSON） | mockaroo.com（有 API） | API 生成 |
| 测试 PDF/Office | W3C test suites、微软官方示例 | 直链 |
| 示例数据库 | sqlite sample databases、AdventureWorks 备份 | 直链 |

## 3D 模型（仅 3D 类 App）

| 用途 | 来源 | 获取方式 |
| --- | --- | --- |
| GLB/GLTF（CC0） | Poly Pizza（Google）、Kenney 3D assets、Quaternius | 直链下载 |

## 调色板 / 主题

| 用途 | 来源 | 获取方式 |
| --- | --- | --- |
| Material 色板 | Material Design 官方 JSON | 结构化文件直接引用 |
| 通用色板 | Open Color、Coolors、Adobe Color | 文件或生成 |

## 优先级建议（覆盖 80% 场景）

1. **Iconify API**（UI 图标）+ **picsum.photos**（占位图）+ **Kenney**（CC0 音效包）
2. **ffmpeg / ImageMagick**（生成音频、视频、渐变图）——零网络依赖
3. 中文字体走 GitHub Releases 直链
4. 测试数据走 dummyjson API

## 许可红线

- 首选 CC0/Public Domain：Kenney、Poly Pizza、OpenMoji、picsum
- Unsplash/Pexels/Pixabay：免费可商用，注意各自条款与署名要求
- Google Fonts：各字体有自己的 OFL 许可，发布时保留声明
- Font Awesome Pro / 任何付费版：不用