# PowerFloat

Windows 无边框置顶悬浮窗，实时显示当前**功耗估算（瓦特）**。适合笔记本看放电功率，或台式机粗看 CPU 负载对应功耗。

- 右键关闭
- 可拖动位置
- 无任务栏图标

## 原理简述

- 笔记本：优先用电池剩余电量变化估算整机功率
- 否则：用 CPU Utility / 占用率 × 估算 TDP 得到近似功耗

数值为粗估，仅供参考。

## 下载与使用

到 [Releases](../../releases) 下载 `PowerFloat.exe`，双击运行即可。

兼容 **Windows 7 / 10 / 11**（.NET Framework 4.x）。

## 自行编译

```bat
build.bat
```

产物：当前目录 `PowerFloat.exe`。
