using System.ComponentModel;

namespace Alife.QQOfficial;

public class QQOfficialConfig
{
    [DisplayName("凭据·AppID")]
    [Description("QQ 开放平台机器人的 AppID（q.qq.com 后台「开发管理」里那串数字 ID）。只进本地配置，插件不会把它打进日志或回显")]
    public string AppId { get; set; } = "";

    [DisplayName("凭据·AppSecret")]
    [Description("与 AppID 配对的机器人密钥。明文存在 Storage\\Configuration 的本插件配置里（平台对所有插件的密钥都是这个模式，没有钥匙串），插件自己不写日志、不进任何返回值")]
    public string AppSecret { get; set; } = "";

    [DisplayName("接口·API 根地址")]
    [Description("留空即官方文档当前写的 https://api.bot.qq.com 。要连沙盒就填 https://sandbox.api.sgroup.qq.com （沙盒域名只在文档示例里出现过，本机未实测）")]
    public string ApiBaseUrl { get; set; } = "";

    [DisplayName("事件·群与私聊消息(1<<25)")]
    [Description("覆盖 C2C_MESSAGE_CREATE、GROUP_AT_MESSAGE_CREATE、GROUP_MESSAGE_CREATE、好友增删、进群退群、收到/退订消息提醒。这是收发的基本位，关掉就什么都收不到")]
    public bool IntentGroupAndPrivate { get; set; } = true;

    [DisplayName("事件·群成员变化(1<<24)")]
    [Description("GROUP_MEMBER_ADD / GROUP_MEMBER_REMOVE / GROUP_JOIN_REQUEST。官方标注是白名单能力，没开权限时整条连接会被平台以关闭码 4014 断掉——那时把这一项关掉")]
    public bool IntentGroupMember { get; set; } = false;

    [DisplayName("事件·按钮互动(1<<26)")]
    [Description("INTERACTION_CREATE：用户点了消息按钮或快捷菜单。要用按钮键盘就开它")]
    public bool IntentInteraction { get; set; } = false;

    [DisplayName("事件·消息审核(1<<27)")]
    [Description("MESSAGE_AUDIT_PASS / MESSAGE_AUDIT_REJECT，内容过审结果")]
    public bool IntentMessageAudit { get; set; } = false;

    [DisplayName("事件·频道与子频道变化(1<<0)")]
    [Description("GUILDS 位：GUILD_CREATE/UPDATE/DELETE 与 CHANNEL_CREATE/UPDATE/DELETE（机器人被拉进频道、频道改名、子频道增删）。只有跑 QQ 频道才需要，默认关")]
    public bool IntentGuilds { get; set; } = false;

    [DisplayName("开关·注入会话")]
    [Description("开启后，QQ 侧收到的消息会以「像好友/群里发来的消息」的形式进入 Alife 会话，由主 Agent 决定要不要回、回什么。关掉就只留发送面，纯当外发通道用")]
    public bool InjectInboundMessages { get; set; } = true;

    [DisplayName("开关·发送消息")]
    [Description("关闭后所有发送类工具不可用；收信与注入不受影响")]
    public bool EnableSend { get; set; } = true;

    [DisplayName("回传·主动消息")]
    [Description("官方对主动下发另有业务配额（群侧未认证 30/qpm、单关系 20/qpm、每群每天 1000 条；单聊侧未认证 5/qps 且 30/qpm），超了报 40034100。被动回复不受这条限制。默认关：不在会话里主动插话")]
    public bool AllowProactiveMessage { get; set; } = false;

    [DisplayName("网络·HTTP 代理")]
    [Description("如 http://127.0.0.1:7890。留空直连。api.bot.qq.com 与它的 wss 网关在本机从未实测可达，连不上时先看这一项、再看 QQOfficialEnv 的探测结果")]
    public string ProxyUrl { get; set; } = "";

    [DisplayName("网络·单次请求超时(秒)")]
    [Description("REST 单次请求超时，默认 20")]
    public int RequestTimeoutSeconds { get; set; } = 20;

    [DisplayName("网络·心跳与重连")]
    [Description("心跳间隔由网关下发（实测协议里是 op 10 Hello 带的 heartbeat_interval，毫秒），本项只影响连不上时的重试间隔，默认 3 秒")]
    public int ReconnectSeconds { get; set; } = 3;

    [DisplayName("语音·silk 编码器")]
    [Description("官方 file_type=3 只认 silk，mp3/wav/m4a 传上去会**掉成文件卡片**（不是语音条）。ffmpeg 只能解 silk 不能编，所以要用语音条必须自己给编码器："
        + "填一个真装了 pilk 的 python.exe 完整路径（自带运行时目录里 Python312 下的那个解释器也行，前提是它确实装上了 pilk），"
        + "参数指向本插件目录里 tools 文件夹下的 to_silk.py（它调 pilk 编码，pcm 采样率必须与「语音·pcm 采样率」一致）。"
        + "注意 pilk 在 PyPI 上只有源码包（0.2.4 没有 cp312 的 Windows 轮子），装它要 Rust 工具链现编——装不上就先看「语音·放行非 silk 直传」能不能解决，或用外部命令先转出 .silk。留空则发语音会明确报错，不会假装成功")]
    public string SilkEncoderPath { get; set; } = "";

    [DisplayName("语音·编码器参数")]
    [Description("与上面配对，占位符 {in}、{out}。例：`convert {in} {out}`")]
    public string SilkEncoderArgs { get; set; } = "";

    [DisplayName("语音·放行非 silk 直传")]
    [Description("官方文档自相矛盾：富媒体概述页写「语音支持 silk/mp3/wav/ogg」，「文件类型与限制」表却只写 silk，第三方实测说 mp3 会掉成文件卡片。"
        + "打开这一项就**不转码、直接把 mp3/wav/ogg 当 file_type=3 上传**，用来自己验一句到底是气泡还是文件卡片；验完建议关掉。默认关（关着时未配编码器会明确拒绝，不会悄悄发成文件）")]
    public bool AllowNonSilkVoice { get; set; } = false;

    [DisplayName("语音·最长秒数")]
    [Description("转码时截断过长的音频，默认 300 秒（5 分钟）")]
    public int VoiceMaxSeconds { get; set; } = 300;

    [DisplayName("提示·输入状态帧数上限")]
    [Description("每个会话一次对话里最多发几帧「正在输入」（msg_type=6 也占被动回复额度），默认 3；填 0=不发")]
    public int TypingMaxFrames { get; set; } = 3;

    [DisplayName("去重·事件保留分钟数")]
    [Description("同一 msg_id 官方可能重复推送，这段时间内见过的 id 直接吞掉，默认 30 分钟")]
    public int DedupTtlMinutes { get; set; } = 30;

    [DisplayName("数据目录")]
    [Description("相对 Alife Storage。放已见消息 id 台账（官方说同一 msg_id 可能重复推送，要按 msg_seq 去重）与各会话被动回复的剩余额度。**不存 token 与密钥**")]
    public string DataFolder { get; set; } = "QQOfficial";

    [DisplayName("调试·详细日志")]
    [Description("记录请求与事件的时间与错误码。**不会**记录 AppSecret 与 access_token")]
    public bool DetailLog { get; set; } = false;
}
