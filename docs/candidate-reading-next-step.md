# 候选读取 UIA 调查

更新：2026-10-12。真实微软拼音 UIA 读取已在本机普通权限 Notepad3 打通；仍保持只读外挂边界，不增加 OCR。用户目前暂停桌面操作，后续只做后台开发，现场复测需恢复授权。

## 已确认根因与有效入口

- 顶层/子窗口枚举看到的 TextInputHost 全屏根不是当前候选入口；真实 CoreWindow 未被 EnumWindows/EnumChildWindows 枚举到，不能据根节点无子树断言 UIA 不可用。
- 先在独立 MTA 上订阅原生 COM UIA3 的 desktop subtree MenuOpened，再重新打开微软候选，可以收到真实事件。事件回调只排队元数据，在观察线程校验系统宿主后沿最多 8 层 Raw 父链取得 HWND；再复用托管 UIA 扫描。
- IME_Candidate_Window 本身 HWND 可以为 0，其父宿主才有有效 HWND；窗口句柄、PID、受保护路径、可见性和前台关联均须验证，不硬编码历史值。
- TEMPLATE_PART_CandidateItemIndex/Text 的 ControlElement 和 ContentElement 都是 false，只遍历 ControlView 会漏编号和文字。候选项内部使用有界 RawView，必须读到有效唯一显式编号，不按位置猜序号。
- 真实候选可包含 ping、emoji、罗马数字和圈号；限制为汉字会导致整页拒读。保留长度/控制字符/编号和身份校验，不把非汉字等同不可信。
- UIA 更新会短暂混合新旧页。连续两次相同 fingerprint、间隔至少 50ms 后发布；候选在确认中使用 Updating 保留旧页，真正消失用 Hidden。窗口/前台失效仍立即清空。
- ChsIME 的 Process.MainModule 查询失败，有限权限 OpenProcess + QueryFullProcessImageName 已验证可读取路径；不需要升权，也不能放宽白名单。

产品新增 UiaEventWindowDiscovery、CandidatePageStability、CandidateReadState；第三方 COM interop 包及 MIT 许可已列入 THIRD_PARTY_NOTICES。没有加入临时诊断的 FlaUI 或使用 ElementFromPoint 作为轮询生产路径；不读取应用正文。

## 实测与回归

| 证据 | 结论 |
| --- | --- |
| artifacts/validation/uia-native-events-20261012.log | desktop 原生订阅收到候选事件；直接订阅旧宿主不能替代 desktop 入口 |
| uia-point-20261012.log / uia-child-hosts-20261012.log | 一次坐标对照只确认受保护系统宿主与有限 Raw 父链；窗口枚举遗漏真实 HWND |
| uia-managed-raw-parts-20261012.log | 编号/文字是 Raw-only 独立节点 |
| uia-raw-before-fix-20261012/ / uia-raw-after-fix-20261012/ | 显式 Raw 编号读取的失败回归与修复后通过 |
| uia-literal-before-fix-20261012/ / uia-literal-after-fix-20261012/ | 非汉字候选页的失败回归与修复后通过 |
| desktop-dictionary-20261012/run1.jsonl | 真实混合过渡页；发现后停止输入、诊断页稳定问题 |
| uia-stability-after-fix-20261012/ | 稳定页修复后核心 30 + Windows 10，共 40 项通过，退出码 0 |
| desktop-dictionary-20261012/run2.jsonl / run3.jsonl | 真实逐键、翻页、词典对应、多列、键盘提交、焦点/隐藏和 owned reader 恢复 |
| background-final-20261012/ | 最新核心 32 + Windows 后台 22，共 54 项通过；过滤 8 项桌面测试 |

以上相对证据路径均位于 artifacts/validation。候选原文日志仅在本机保存。完整用例和限制见 [本地词典测试](local-dictionary-testing.md)。

## 续接时避免误判

1. 监听必须先启动再重开候选；旧宿主直接订阅无事件不代表 desktop 无事件。
2. 先核对诊断 PID/限时/日志写入时间；诊断结束后的旧日志不能代表新输入。
3. 不放宽读取范围或权限，不把 UIA 的瞬时过渡页当稳定候选。
4. Computer Use 鼠标点微软候选跨到 TextInputHost，工具目标校验会拒绝；若不可选该宿主就停当轮，不绕过，后续由用户手动验收。
5. 新窗口缓冲/主题代码只通过后台离屏回归，不能把更早的真实输入结果当作新交互现场通过。DPI、其他应用及旧版微软拼音模式另测。

## 官方依据

- [UIA 事件说明](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-eventsoverview)：提供程序可能按订阅情况发出事件；监听时序影响观察结果。
- [UIA 线程说明](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading)：独立 MTA 上管理 UIA 工作及事件订阅。
- [ElementFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-elementfrompoint)：用于一次只读入口对照，不作为扩大读取范围的理由。

API 文档不是本机兼容性证明；具体结论以记录的目标环境实测为准。
