# Alife.QQOfficial

接 **QQ 开放平台的官方机器人**（q.qq.com 后台那套 OpenAPI）：自己取 `access_token` 并续期、自己连 WebSocket 网关收事件、把 QQ 侧消息按会话形状注入 Alife，主 Agent 的回信再由本插件发回 QQ。群管理、富媒体、分片上传、素材图片、流式打字机、频道、快捷菜单与指令面板都在里面。

目标 ID 全是 32 位十六进制 openid，不是 QQ 号也不是群号。只依赖 `Alife.Function.FunctionCaller`，不需要任何 NuGet 包。

## 版本
5.0.0（对应 Alife 客户端 5.0.x；本机 5.0.2 在役客户端已热编译装载成功）

## 5.0.0 的改动
- 版本号对齐客户端主版本（官方约定：插件版本首位应与客户端一致，`PluginSystem.IsClientCompatible` 按主版本判定）
- 清单新增 `Environments.pip: imageio-ffmpeg`：ffmpeg 那一步由宿主同步插件环境时自动带上，转 silk 的解码步骤不再要求你手动装 ffmpeg（编码仍要你自备 pilk，见「语音·silk 编码器」说明）
- ffmpeg 查找顺序改为：自带运行时的 imageio-ffmpeg → PATH；可执行文件后缀按平台取，不再写死 `.exe`
- 修正「语音·silk 编码器」的说明文本：原来那串示例路径被转义吃掉过（`	` 变成制表符），现在不带写死的盘符路径

## 一句话架构
**Alife 自己就是那个 Agent。** 本插件里没有一条业务判断——谁该回、回什么、要不要合并成一条，全是主 Agent 决定的；插件只管协议层该管的事：token 生命周期、网关握手与重连、事件去重、被动回复额度记账、错误码翻译成人话。

## 依赖
- Alife.Function.FunctionCaller
- 外部：q.qq.com 上一个状态正常的机器人，后台「开发管理」里的 AppID + AppSecret

## 配置
| 键 | 默认 | 说明 |
|---|---|---|
| AppID / AppSecret | 空 | 只进本地配置文件，不写日志、不进任何返回值 |
| ApiBaseUrl | 空 | 空即 `https://api.bot.qq.com`；沙盒填 `https://sandbox.api.sgroup.qq.com`（沙盒域名本机未实测） |
| IntentGroupAndPrivate | 开 | `1<<25`，收发的基本位；关掉等于什么都收不到 |
| IntentGroupMember | 关 | `1<<24`，群成员变化与入群申请。**白名单能力，没权限时网关直接以 4014 关连接、不给原因** |
| IntentInteraction | 关 | `1<<26`，按钮/菜单点击回调 |
| IntentMessageAudit | 关 | `1<<27`，消息审核结果 |
| IntentGuilds | 关 | `1<<0`（GUILDS），频道与子频道增改删事件。只有跑 QQ 频道才需要 |
| InjectInboundMessages | 开 | 关掉就只留发送面，QQ 来的消息不进会话 |
| EnableSend | 开 | 关掉后所有发送类工具不可用 |
| AllowProactiveMessage | 关 | 没有 msg_id 时是否允许主动下发（平台另有业务配额） |
| ProxyUrl | 空 | HTTP 代理。`api.bot.qq.com` 与它的 wss 网关**本机从未验证过可达**，连不上先填这个试试 |
| RequestTimeoutSeconds | 20 | REST 单次超时（钳 5~120） |
| ReconnectSeconds | 3 | 断线重连间隔（心跳间隔不是这项，是网关下发的） |
| DataFolder | `QQOfficial` | 预留的数据目录 |
| DetailLog | 关 | 记请求/事件与错误码；**不会**记 AppSecret 与 token |

## AI 函数（46 个）
**收发**：`QQSend`（会话出口，`FunctionMode.Content`）、`QQSendText`（文本/markdown，带按钮时给 `keyboardJson`）、`QQSendArk`（结构化卡片，模板只认 23/24/37、需向平台运营申请准入）、`QQSendMedia`（图/视频/语音/文件，自动选整文件或分片）、`QQRecall`、`QQStreamText`（单聊打字机）、`QQTypingNotify`（输入状态提示）
**素材**：`QQUploadMarkdownImage`（拿 `qqbot-media://` 引用）、`QQMarkdownImages`、`QQDeleteMarkdownImage`
**群与成员**：`QQGroupInfo`、`QQGroupBotState`、`QQGroupMembers`（游标自翻页）、`QQGroupMemberInfo`、`QQGroupMuteMembers`、`QQGroupMuteSetting`、`QQGroupBlacklist`、`QQGroupBlacklistOperate`、`QQGroupRemoveMembers`
**入群审批**：`QQGroupJoinRequests`、`QQGroupApproveJoinRequest`、`QQJoinApprovalStrategies`、`QQCreateJoinApprovalStrategy`、`QQUpdateJoinApprovalStrategy`、`QQDeleteJoinApprovalStrategy`、`QQExecuteJoinApprovalStrategy`、`QQJoinApprovalWhitelist`
**互动/菜单/面板/频道**：`QQAnswerInteraction`、`QQMenuGet`、`QQMenuPut`、`QQPanels`、`QQCreatePanel`、`QQPanelInfo`、`QQUpdatePanel`、`QQPanelTargets`、`QQGenerateShareLink`、`QQMyGuilds`、`QQGuildChannels`、`QQGuildChannelInfo`、`QQCreateGuildChannel`、`QQUpdateGuildChannel`
**自检**：`QQOfficialEnv`

## 谁在说话：昵称与 QQ 号
**这是官方接口的硬限制，不是实现问题**：单聊事件 `C2C_MESSAGE_CREATE` 的 `author.username` **恒为空串**（官方示例就是 `""`），而 OpenAPI **没有任何"按 openid 查用户资料"的接口**（`/users/@me` 只回机器人自己），`union_openid`/`union_user_account` 要特殊申请且内邀。

所以插件内置一本**跨场景共享的通讯录**（`QQIdentityStore`，落盘 `Storage\QQOfficial\identities.json`）：

- 破解点是实测事实——**私聊的 `user_openid` 与群里的 `member_openid` 是同一个值**，所以群里认识过的人，私聊也认得；第二个来源是引用消息（`message_type=103` 时 `msg_elements[].author` 是完整 User 对象）；`mentions[]` 也喂
- 昵称与记录不同就覆盖 ⇒ **用户改名自动跟随**；消息路径上**零 I/O**（只标脏，`OnUpdate` 每 30 秒落盘一次，卸载时再落一次）
- 注入显示优先级：备注名 → 昵称／QQ 号 → 「昵称未知」，绝不拿昵称当身份
- **QQ 号只有三条有依据的路**：① `QQBindIdentity` 人工绑；② 内邀开通后事件里带 `union_user_account`，自动记；③ 与**另一条 QQ 通道**对撞——把 `StorageQQOfficialqq_peers.txt`（每行 昵称<TAB>QQ号）里的唯一昵称配过去，**重名一律不配**（猜错会把人认成别人）。这条要有数据，得由那条通道的接入方去写那份文件，本插件只读
- 工具：`QQIdentityShow`（看通讯录）、`QQBindIdentity`（绑 QQ 号/备注名）、`QQLearnGroupMembers`（用群成员接口一次性回填全群昵称与群名，内邀接口，没权限回 11253 并如实报）、`QQFindPerson`（按昵称/备注名搜出 openid——**要 @ 人必须先拿 openid**）

## @ 人、引用回复、语音条的三条硬规矩
- **@ 人**：正文里的 @ 是富文本标记 `<@openid>`，**只有 markdown 消息能承载**，纯文本发出去会显示成那串字符——所以带 @ 时代码自动按 markdown 发（新式 `<qqbot-at-user id=…/>` 实测被当纯文本，故仍用 legacy 形态）
- **引用回复**：`message_reference` 与 markdown **互斥**，同时给会被平台只认一个。这里直接拒绝并说明，不悄悄丢内容
- **语音条**：官方限制表里 `file_type=3` 只写 silk，而 **ffmpeg 只能解码 silk、不能编码**（编码要 Silk 专有实现，热编译插件装不了 NuGet 包）。两条路插件里都备好了：
  - **先验一次，几乎零成本**：官方文档自相矛盾——富媒体概述页写「语音支持 **silk/mp3/wav/ogg**，发送后展示语音条」，限制表却只写 silk，第三方实测说 mp3 会掉成文件卡片。打开配置「语音·放行非 silk 直传」，直接把 mp3/wav 按 `file_type=3` 发一次，看真接口给的是气泡还是文件卡片；是气泡就不用转码，验完建议关掉（关着又没配编码器时插件**明确拒绝**，不会悄悄发成文件）
  - **要转码就走 pilk**：`tools/to_silk.py` 已随插件附上（`pilk.encode(pcm, silk, pcm_rate=…, tencent=True)`，`tencent=True` 是腾讯变体 silk）。pilk 只发到 **cp311** 的 Windows 轮子，本机有 uv 装的 CPython 3.11.15 ⇒ 拿它 `pip install pilk` **免编译**（Alife 自带 3.12 与系统 3.14 都没有轮子，别拿它们装）。配置：「语音·silk 编码器」填解释器路径，「语音·编码器参数」填 `"…	ools	o_silk.py" {in} {out}`。插件先用 ffmpeg 解成 pcm（按 `VoiceMaxSeconds` 截断），**`-ar` 与 wrapper 的 `pcm_rate` 同为 24000，两边必须一致**否则语速变调；任一步失败都把 ffmpeg/pilk 的 stderr 原样报出来

## 目标 ID
`groupOpenid` / `userOpenid` / `memberOpenid` 都是 32 位十六进制串，**不是 QQ 号**。`user_openid`（单聊）与 `member_openid`（群里）是同一个人的两种场景标识。官方**没有**「列出机器人所在的群」的接口，所以插件无法自己枚举群——只有收过消息的群才有 openid。

## 消息怎么进会话
```
[消息来源(Alife.QQOfficial)]            ← 平台 Interactor 自己加的前缀
[QQ官方群聊消息(group_openid)]           ← 本插件加的来源行
[member_openid(昵称)]:正文               ← 说话人与内容
[附件:图片] https://…                   ← 附件按 content_type 标注，语音另带平台 ASR 文本
```
故意**不复用** `[群聊消息(` 这个前缀——宿主里有别的消息通道拿它当"这个群已被占用"的判据，撞上了会互相误判。回声靠启动时取的自身 openid 硬挡，不信昵称（谁都能把昵称改成机器人的名字）。

## 注册方式
`RegisterHandler` 走框架默认的 **Explicit**（不挂隐式）：46 个函数的文档直接进"当前可用功能"，约 **6.0K 字符**常驻提示，另加本插件的说明与详细描述。不再依赖模型先去调 `<模块名/>` 拉文档——这条通道要随时可用，藏一层入口容易让它根本不去看。

## 平台规则（决定话术的几条）
- 被动回复窗口：群 5 分钟内最多 5 次、单聊 60 分钟内最多 4 次；超了平台回 `40034128`。插件自己按 msg_id 记次数，用完直接说明原因，不再白打一次接口
- 主动消息是另一套配额（群侧未认证 30/qpm、每群每天 1000 条；单聊未认证 5/qps 且 30/qpm），超了 `40034100`，没权限 `40034105`；默认关
- 同一 `msg_id` + 同一 `msg_seq` 重复发会被去重（`40054005`）；所以**POST/PUT/DELETE 一律不自动重试**，只有 GET 网络层失败重试一次
- 失败判定只认数字码：`/v2` 业务接口看 `err_code`，`getAppAccessToken` 看 `code`（且失败时 HTTP 仍是 200）。官方原话：不要依据 `message` 判定成败
- 白名单/内邀权限（群信息、成员列表、bot_state、单人详情、黑名单、批量移除等）没开时回 `11253`，插件原样报出并说明是权限没开
- 网关关闭码 4001/4002/4010/4011/4012/4013/4014 属于"重发 IDENTIFY 也没用"，连撞 3 次就停止重连并说明该关哪一位（4014 就是 intents 里有位没权限）

## 验证口径（重要）
`api.bot.qq.com` **已用真凭据跑通过一段**：token 取到了、群消息发送接口通了（第一次真反馈就是 `HTTP 400 + err_code 40054005`，暴露出本插件没发 `msg_seq` 的问题，已在 4.0.0 内修掉：同一 `msg_id` 现在自动递增 `msg_seq`，且非 2xx 时先读 body 的 `err_code` 再退化成 HTTP 状态码）。但**"改完就好"这句我没有依据**——修完还没在真接口复跑。其余仍是离线验证：

2026-10-24 用同一份凭据只做只读复验（没发任何消息）：`POST /app/getAppAccessToken` 实回 79 字符 token 与 `expires_in=7200`；`GET /gateway/bot` 实回 `url = wss://api.sgroup.qq.com/websocket`（生产网关在 sgroup 域名，不是 api.bot.qq.com）、`shards`、`session_start_limit{max_concurrency,remaining,reset_after,total}`，**没有 `sessions` 这个键**，本插件按 `url` 走所以不受影响。`GET /v2/users/@me/guilds` 与 `/v2/guilds/mine/groups` 实回 `err_code 40011002` / `code 11001` / `message 不支持的调用`——这个 app 的能力集里没有频道类接口，两条码已收进码表。仍未复跑的还是那句：**真发出一条群消息并撤回**。

- 编译门 `.buildcheck/Alife.QQOfficialHostCheck.csproj`（引在役客户端实现程序集，等价热编译）`--no-incremental` **0 错误**（CA1416/MSB3246 是实现集带来的噪音，判据只看 error）
- `scratch/QQOfficialProbe` 反射探针 **200 项 0 失败**（`bash scratch/QQOfficialProbe/run.sh` 一条命令跑完，自己起停两个假服务）：JSON 取值（数字/字符串两种形态、Unix 秒与 RFC3339 时间戳）、错误判定（`err_code`/`code`/0 值/数组体/`msg` 兜底）与码表话术、intents 位组合与 4014 提示、键盘 ≤5×≤5 与 `style∈{0,1}` 与 `action.data≤100` 校验、ark 模板只认 23/24/37 与 kv 的 value/obj 形状、菜单 10 项/子菜单 5 项/名字权重（汉字算 2）/链接必须 https、被动回复额度（群 5 次、单聊 4 次、窗口过期放行）、事件按 id 去重、路径拼装
- 两个假服务把"未实机"这段的空补齐了大半：`scratch/qqofficial-mock.mjs` 是官方 OpenAPI 的 HTTP 假服务，`scratch/qqofficial-wsmock.mjs` 是网关的**手写 RFC6455 WebSocket 假服务**（本机没装 `ws` 包）。验到真握手与帧这一层：Hello 下发 `heartbeat_interval` → op2 IDENTIFY（token 前缀 / intents 位 / shard / properties）→ `READY` 拿 session → 心跳带回最新 `s` 并收 op11 → 平台发 op7 后确实重连；另两种异常剧本也演了：**4014 连撞三次停止重连**、**op9 会话作废后从 RESUME 退回 IDENTIFY**。HTTP 侧验了 token 缓存与提前续期、鉴权头、`err_code` 抛出、GET 重试一次而 POST 一次都不重试、网关地址问不到时的报错
- 端点形状逐个抓自官方文档当前版本（`autogen/api/*`、素材管理与富媒体概述页、变更记录），不是我凭印象写的

探针抓出四个编译门完全挡不住的真缺陷，都已修：① `QQAccessToken`/`QQGatewayClient` 把**共享** HttpClient 用 `using` 释放，第二次请求就 `ObjectDisposedException`；② `StringContent` 的媒体类型写成 `application/json; charset=utf-8` 被 .NET 判非法（**每一次带请求体的调用都会炸**）；③ HTTP 5xx 带 `{}`（没有 `err_code`）被当成功返回；④ 平台以 4014 关连接时"连撞三次就停手"永远不触发——致命关闭走的是正常返回，而循环末尾无条件把计数清零了，改成"只有真的 READY 过才清零"。另外启动时机修了一处：连接从 `OnAwake` 挪到 `OnStart`（`OnAwake` 时平台还没注入 `Configuration`，读 AppID 会 NRE 并被框架吞掉，表现成"装了但静默不连"）。

## 事件覆盖
文档列的 **21 类事件全部有处理**：三类消息（`C2C_MESSAGE_CREATE`、`GROUP_AT_MESSAGE_CREATE`、`GROUP_MESSAGE_CREATE`）注入会话；`INTERACTION_CREATE` 里 type 11/12 自动回执、其余（13 反馈、14 清空会话、15 故事集、16 切换模型、18 用户授权、19 群授权、20 授权状态变更）播报给模型；`FRIEND_ADD/DEL`、`C2C_MSG_RECEIVE/REJECT`、`GROUP_ADD_ROBOT`、`GROUP_DEL_ROBOT`、`GROUP_MSG_RECEIVE/REJECT`、`GROUP_MEMBER_ADD/REMOVE`、`GROUP_JOIN_REQUEST`、`MESSAGE_AUDIT_PASS/REJECT`、`SUBSCRIBE_MESSAGE_STATUS`、`GUILD_CREATE/UPDATE/DELETE`、`CHANNEL_CREATE/UPDATE/DELETE` 走 `[QQ官方群事件]` / `[QQ官方频道事件]` 的杂项推送。频道那 6 类要开 `IntentGuilds(1<<0)` 才会推。

## 还没做
- 真凭据下的实机链路：token 实取、网关握手、收发消息、上传与素材、群管与审批、菜单面板、流式
- **embed 消息没做**：官方文档的适用矩阵明写「机器人发送：单聊不支持、群聊不支持、文字子频道支持、频道私信支持」，而**频道方向没有对应的 v2 发送端点**（47 个 `autogen/api` 里不含频道消息发送），所以不编 URL 硬做
- ark 已做，但平台写着「达到准入条件并向运营申请后获得权限」，没权限会被拒；模板 ID 只认文档列的 23 / 24 / 37
- `URL 报备` 这条我没在文档里定位到，需要时再查
- 往频道/子频道里发消息（频道侧只做了频道与子频道本身的查改删）

解压到 `Alife\Storage\Plugins` 即可（`manifest.json` + 12 个 `.cs`，热编译，不需要任何 NuGet 包）。装完在配置里填 AppID/AppSecret，先跑 `QQOfficialEnv` 看它走到哪一步。
