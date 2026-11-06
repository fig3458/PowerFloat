# PowerFloat

Windows **无边框置顶悬浮窗**，实时显示当前功耗估算（瓦特）。适合笔记本看放电功率，或台式机粗看 CPU 负载对应功耗。

## 功能

- 悬浮显示功率（W），可拖动位置
- 右键关闭；无任务栏图标
- 笔记本优先用电池电量变化估算整机功率
- 否则用 CPU 占用 × 估算 TDP 得到近似值

> 数值为粗估，仅供参考，不能替代专业功率计。

## 下载与使用

1. 到 [Releases](https://github.com/fig3458/PowerFloat/releases) 下载 `PowerFloat.exe`
2. 双击运行即可
3. 拖到合适位置；不用时右键关闭

兼容 **Windows 7 / 10 / 11**（.NET Framework 4.x）。

## 自行编译

```bat
build.bat
```

产物：当前目录 `PowerFloat.exe`。

## 许可证

[MIT](LICENSE)
