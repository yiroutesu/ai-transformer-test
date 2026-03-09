using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class TranslationClient : MonoBehaviour
{
    [Header("Server Config")]
    public string serverUrl = "http://127.0.0.1:8000/translate";

    [TextArea(3, 5)]
    public string sourceText = "你好，世界！";

    public string sourceLang = "zh";
    public string targetLang = "en";
    
    /// <summary>
    /// 翻译回调委托
    /// </summary>
    public static event Action<string> OnTranslationReceived;

    private void Start()
    {
        StartCoroutine(RequestTranslation());
    }

    /// <summary>
    /// 调用这个就行了
    /// </summary>
    /// <param name="requestData"></param>
    public void StartTranslate(TranslateRequestData requestData)
    {

        StartCoroutine(RequestTranslation(requestData));
    }
    

    IEnumerator RequestTranslation()
    {
        // 构造 JSON
        string json = JsonUtility.ToJson(new TranslateRequestData
        {
            text = sourceText,
            source_lang = sourceLang,
            target_lang = targetLang
        });

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        UnityWebRequest request = new UnityWebRequest(serverUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        Debug.Log("Sending translation request...");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Request failed: " + request.error);
            yield break;
        }

        Debug.Log("Raw response: " + request.downloadHandler.text);

        // 解析返回 JSON
        TranslateResponse response =
            JsonUtility.FromJson<TranslateResponse>(request.downloadHandler.text);

        Debug.Log("Translated Text: " + response.translated_text);
    }
    
    IEnumerator RequestTranslation(TranslateRequestData requestData)
    {
        // 构造 JSON
        string json = JsonUtility.ToJson(new TranslateRequestData
        {
            text = requestData.text,
            source_lang = sourceLang,
            target_lang = requestData.target_lang
        });

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        UnityWebRequest request = new UnityWebRequest(serverUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        Debug.Log("Sending translation request...");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Request failed: " + request.error);
            yield break;
        }

        Debug.Log("Raw response: " + request.downloadHandler.text);

        // 解析返回 JSON
        TranslateResponse response =
            JsonUtility.FromJson<TranslateResponse>(request.downloadHandler.text);

        Debug.Log("Translated Text: " + response.translated_text);
        OnTranslationReceived?.Invoke(response.translated_text);
    }
    

    // ===== 请求 / 响应结构 =====

    [System.Serializable]
    public class TranslateRequestData
    {
        public string text;
        public string source_lang;
        public string target_lang;
    }

    [System.Serializable]
    class TranslateResponse
    {
        public string source_text;
        public string translated_text;
        public string source_lang;
        public string target_lang;
        public float time_cost;
    }
}
