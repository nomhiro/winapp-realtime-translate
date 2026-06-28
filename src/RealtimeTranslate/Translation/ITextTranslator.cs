using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RealtimeTranslate.Translation;

/// <summary>
/// テキスト翻訳の抽象。既定実装は Azure Translator（低遅延）、オプションで Azure OpenAI（高品質）。
/// </summary>
public interface ITextTranslator
{
    /// <summary>一括翻訳して全文を返す。</summary>
    Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// 訳文を逐次（増分）で返す。ストリーミング非対応の実装は最終結果を1回だけ返す。
    /// UI で訳文を逐次表示するために使う。
    /// </summary>
    IAsyncEnumerable<string> TranslateStreamAsync(string text, CancellationToken cancellationToken = default);
}
