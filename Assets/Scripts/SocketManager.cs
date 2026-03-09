using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;

public class SocketManager : MonoBehaviour
{
    Socket socket;
    public TMP_InputField host;
    public TMP_InputField port;
    public TMP_InputField body;
    public Transform messageContainer;
    public GameObject messageItemPrefab;

    public TMP_Text clienText; // 连接状态提示

    public TranslationClient translationClient; // 引用你的翻译组件

    const int BUFFER_SIZE = 1024;
    byte[] readBuff = new byte[BUFFER_SIZE];

    ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();
    ConcurrentQueue<string> errorQueue = new ConcurrentQueue<string>();

    private List<MessageItemData> allMessages = new List<MessageItemData>();

    [Serializable]
    private class MessageItemData
    {
        public string addressPrefix;   // 如 "127.0.0.1:54321"
        public string originalContent; // 如 "你好"
        public TMP_Text textComponent; // UI 文本组件
    }

    // 当前正在翻译的消息（用于回调时知道更新哪条）
    private MessageItemData currentTranslatingMessage;

    void Start()
    {
        // 订阅翻译完成事件（你的 TranslationClient 已定义）
        TranslationClient.OnTranslationReceived += OnTranslationReceived;
    }

    void Update()
    {
        while (messageQueue.TryDequeue(out string msg))
        {
            ProcessMessage(msg);
        }

        while (errorQueue.TryDequeue(out string err))
        {
            Debug.LogError(err);
            clienText.text = err;
        }
    }

    void ProcessMessage(string str)
    {
        if (str.StartsWith("GetMsg"))
        {
            string[] parts = str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < parts.Length; i++)
            {
                ParseAndAddMessage(parts[i]);
            }
        }
        else
        {
            ParseAndAddMessage(str);
        }
    }

    void ParseAndAddMessage(string rawMessage)
    {
        string prefix = "";
        string content = rawMessage;

        // 尝试提取 "IP:PORT" 前缀（第一个冒号之前，且包含数字或点）
        int firstColon = rawMessage.IndexOf(':');
        if (firstColon > 0 && firstColon < rawMessage.Length - 1)
        {
            string potentialPrefix = rawMessage.Substring(0, firstColon);
            // 简单判断是否像地址（包含 . 或 数字）
            if (potentialPrefix.Contains(".") || char.IsDigit(potentialPrefix[0]))
            {
                prefix = potentialPrefix;
                content = rawMessage.Substring(firstColon + 1);
            }
        }

        AddMessage(prefix, content);
    }

    void AddMessage(string addressPrefix, string content)
    {
        if (allMessages.Count >= 100)
        {
            var oldest = messageContainer.GetChild(0);
            Destroy(oldest.gameObject);
            allMessages.RemoveAt(0);
        }

        string displayText = string.IsNullOrEmpty(addressPrefix)
            ? content
            : $"{addressPrefix}:{content}";

        var itemObj = Instantiate(messageItemPrefab, messageContainer);
        var textComp = itemObj.GetComponentInChildren<TMP_Text>();
        textComp.text = displayText;

        var msgData = new MessageItemData
        {
            addressPrefix = addressPrefix,
            originalContent = content,
            textComponent = textComp
        };

        Button btn = itemObj.GetComponentInChildren<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(() =>
            {
                TranslateMessage(msgData);
            });
        }

        allMessages.Add(msgData);
    }

    void TranslateMessage(MessageItemData msgData)
    {
        if (currentTranslatingMessage != null)
        {
            Debug.Log("请等待当前翻译完成");
            return;
        }
        if (string.IsNullOrWhiteSpace(msgData.originalContent))
        {
            string newText = string.IsNullOrEmpty(msgData.addressPrefix)
                ? "[提示] 无法翻译空消息"
                : $"{msgData.addressPrefix}:[提示] 无法翻译空消息";
            msgData.textComponent.text = newText;
            return;
        }

        if (translationClient == null)
        {
            Debug.LogWarning("Translation Client is null");
            string newText = string.IsNullOrEmpty(msgData.addressPrefix)
                ? "[错误] 未绑定翻译组件"
                : $"{msgData.addressPrefix}:[错误] 未绑定翻译组件";
            msgData.textComponent.text = newText;
            return;
        }

        // 显示“翻译中”
        string translatingText = string.IsNullOrEmpty(msgData.addressPrefix)
            ? "[翻译中...]"
            : $"{msgData.addressPrefix}:[翻译中...]";
        msgData.textComponent.text = translatingText;

        // 保存当前翻译目标（用于回调）
        currentTranslatingMessage = msgData;

        // 构造请求数据（使用你的 TranslationClient 的结构）
        var requestData = new TranslationClient.TranslateRequestData
        {
            text = msgData.originalContent,      // ✅ 只传内容
            source_lang = "auto",
            target_lang = "en"
        };

        // 调用你的方法（完全兼容）
        translationClient.StartTranslate(requestData);
    }

    // 回调：当 TranslationClient 触发 OnTranslationReceived
    void OnTranslationReceived(string translatedText)
    {
        if (currentTranslatingMessage != null && currentTranslatingMessage.textComponent != null)
        {
            string finalText = string.IsNullOrEmpty(currentTranslatingMessage.addressPrefix)
                ? $"[译] {translatedText}"
                : $"{currentTranslatingMessage.addressPrefix}:[译] {translatedText}";

            currentTranslatingMessage.textComponent.text = finalText;
            currentTranslatingMessage = null; // 清空引用
        }
    }

    // ===== 网络通信部分（保持不变）=====

    public void ConnectBtn()
    {
        try
        {
            string h = host.text;
            int p = int.Parse(port.text);
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(h, p);
            socket.BeginReceive(readBuff, 0, BUFFER_SIZE, SocketFlags.None, ReceiveCb, null);
            SendToServer("GetMsg\n");
            clienText.text = "已连接";
        }
        catch (Exception ex)
        {
            errorQueue.Enqueue($"[Connect Error] {ex.Message}");
        }
    }

    public void SendBtn()
    {
        if (socket?.Connected == true && !string.IsNullOrEmpty(body.text))
        {
            SendToServer(body.text + "\n");
            body.text = "";
        }
    }

    void SendToServer(string sendStr)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(sendStr);
            socket.Send(data);
        }
        catch (Exception ex)
        {
            errorQueue.Enqueue($"[Send Error] {ex.Message}");
        }
    }

    void ReceiveCb(IAsyncResult ar)
    {
        try
        {
            int count = socket.EndReceive(ar);
            if (count <= 0)
            {
                errorQueue.Enqueue("服务器断开连接");
                return;
            }

            string recvStr = Encoding.UTF8.GetString(readBuff, 0, count);
            messageQueue.Enqueue(recvStr);

            socket.BeginReceive(readBuff, 0, BUFFER_SIZE, SocketFlags.None, ReceiveCb, null);
        }
        catch (Exception ex)
        {
            errorQueue.Enqueue($"[Receive Error] {ex.Message}");
        }
    }

    void OnDestroy()
    {
        socket?.Close();
        TranslationClient.OnTranslationReceived -= OnTranslationReceived;
    }
}