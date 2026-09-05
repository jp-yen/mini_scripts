namespace FontSelector.Services;

/// <summary>
/// 言語別・絵文字のプレビュー文例データを提供するプロバイダクラス。
/// </summary>
public static class SampleTextProvider
{
    public const string DefaultPreviewText =
        "The quick brown fox jumps over the lazy dog.\n" +
        "あいうえおかきくけこ さしすせそ 漢字テスト 永遠 東京\n" +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789\n" +
        "i1llI|! oοｏ0OＯ rn m vv w cl d S 5 $ B 8 Z 2\n" +
        "ー ― - － 一 —— ―― ソンシツ口ロ";

    public const string EmojiSampleText =
        "【Unicode 1.1〜6.0 (初期・基本絵文字)】\n" +
        "☺ ☻ ☕ ♨ ✈ ✉ ❄ ☀ ☁ ☂ ⚽ ⛄ ♠ ♥ ♦ ♣ 🐱 🐶 🍎 🚗 🚀 🏠 🎉 💡 🔥\n" +
        "【Unicode 7.0〜8.0 (2014-2015 / 肌色スキントーン・日常シンボル)】\n" +
        "🌶 🌭 🌮 🌯 🍿 🍾 🦄 🐿 🦀 🦁 🏹 🧀 🖐 👋🏻 👋🏽 👋🏿 👍🏻 👍🏿 🕵 🕊\n" +
        "【Unicode 9.0〜10.0 (2016-2017 / 感情・表情・ファンタジー)】\n" +
        "🤣 🤤 🤢 🤧 🤠 🤡 🤥 🥞 🥓 🥐 🥑 🦆 🦇 🦉 🦊 🦋 🧙 🧚 🧛 🧜 🧝 🧟 🦕 🦖 🥩 🥨 🥦 🥟\n" +
        "【Unicode 11.0〜12.0 (2018-2019 / 動物・アクセシビリティ・食べ物)】\n" +
        "🥰 🥵 🥶 🥳 🥺 🦹 🦝 🦙 🦛 🥯 🧁 🧂 🦮 🦯 🦼 🦽 🧇 🧆 🧈 🧃 🪓 🪀 🪐 🦩 🦥 🦦 🦨 🧎 🧑‍🤝‍🧑\n" +
        "【Unicode 13.0〜14.0 (2020-2021 / 手のジェスチャー・多様性・道具)】\n" +
        "🥲 🥸 🤌 🫀 🫁 🥷 🦤 🦭 🫒 🫓 🫖 🪟 🪞 🪤 🪦 🫠 🫢 🫣 🫡 🫥 🫦 🫧 🫱 🫲 🫶 🫅 🫄 🫘 🫙 🪪 🪩\n" +
        "【Unicode 15.0〜16.0 (2022-2024 / 最新絵文字)】\n" +
        "🫨 🩷 🩵 🩶 🫸 🫷 🪿 🪽 🪼 🫚 🪻 🪭 🪮 🪈 🪯 🛜 🐦‍⬛ 🍋‍🟩 🍄‍🟫 ⛓️‍💥 🧑‍🧑‍🧒\n" +
        "【ZWJ結合・複合絵文字】\n" +
        "👨‍👩‍👧‍👦 🧑‍💻 👩‍🔬 👨‍🚀 🐱‍👤 🏳️‍🌈 🏴‍☠️";

    private static readonly Dictionary<string, string> LanguageSampleTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["日本語"] = "𠮷野の山に ふりつむ雪の 𣘺を渡れば、\n𛀵るの野に 鶯鳴きて 木の芽の雫 𛁞にほひけり。生𛁛𛂦゙ う𛂂ぎ\nABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789\nThe quick brown fox jumps over the lazy dog.\ni1llI|! oοｏ0OＯ rn m vv w S 5 $ B 8 Z 2\nー ― - － 一 ソ ン シ ツ 口 ロ",
        ["英語 / ラテン文字"] = "The quick brown fox jumps over the lazy dog.\nPortez ce vieux whisky au juge blond qui fume.\nZwölf Boxkämpfer jagen Viktor quer über den großen Sylter Deich.\nDès Noël, où un zéphyr haï me vêt de glaçons mœlleux.\nEl veloz murciélago hindú comía feliz cardillo y kiwi.\n0123456789 (!@#$%^&*.,?:;- €$£¥ §«»„” “”)\ni1llI|! oοｏ0OＯ rn m vv w cl d S 5 $ B 8 Z 2",
        ["英語 / ラテン文字 (私用領域・Symbol)"] = "The quick brown fox jumps over the lazy dog.\nPortez ce vieux whisky au juge blond qui fume.\nZwölf Boxkämpfer jagen Viktor quer über den großen Sylter Deich.\nDès Noël, où un zéphyr haï me vêt de glaçons mœlleux.\nEl veloz murciélago hindú comía feliz cardillo y kiwi.\n0123456789 (!@#$%^&*.,?:;- €$£¥ §«»„” “”)\ni1llI|! oοｏ0OＯ rn m vv w cl d S 5 $ B 8 Z 2",
        ["韓国語"] = "나랏말이 중국과 달라 한자와는 서로 통하지 아니하여,\n백성이 말하고자 하는 バ가 있어도 뜻を 펴지 못한다.\n0123456789\nThe quick brown fox jumps over the lazy dog.",
        ["中国語 (簡体)"] = "欢迎来到中国，请点击链接查看详细信息。\n我们随时为您提供优质的专业服务与帮助。\n0123456789\nThe quick brown fox jumps over the lazy dog.",
        ["中国語 (繁体)"] = "歡迎來到臺灣，請點擊連結查看詳細資訊。\n我們隨時為您提供優質的專業服務與協助。\n0123456789\nThe quick brown fox jumps over the lazy dog.",
        ["キリル文字"] = "В человеке всё должно быть прекрасно:\nи лицо, и одежда, и душа, и мысли.\n0123456789\nThe quick brown fox jumps over the lazy dog.",
        ["ギリシャ文字"] = "Ξεσκεπάζω την ψυχοφθόρα βδελυγμία.\nΔιατηρώντας αναλλοίωτη την κλασική μορφή της γλώσσας.\n0123456789\nThe quick brown fox jumps over the lazy dog.",
        ["アラビア文字"] = "نص حكيم له سر قاطع وذو شأن عظيم مكتوب على ثوب أخضر ومغلف بجلد.\nأبجد هوز حطي كلمن سعفص قرشت ثخذ ضظغ\n0123456789\nThe quick brown fox jumps over the lazy dog.",
        ["ヘブライ文字"] = "דג סקרן שט בים מאוכזב ולפתע מצא חברה.\nאיך בלconnection תפסה גשם חם עם כדור קטן?\n0123456789\nThe quick brown fox jumps over the lazy dog.",
        ["タイ文字"] = "เป็นมนุษย์สุดประเสริฐเลิศคุณค่า กว่าบรรดาฝูงสัตว์เดรัจฉาน\nจงฝึกตนให้เป็นคนดีมีปัญญา รู้รักษาตัวรอดเป็นยอดดี\n0123456789\nThe quick brown fox jumps over the lazy dog.",
        ["デーヴァナーガリー"] = "ऋषियों को सताने वाले दुष्ट राक्षसों के राजा रावण का सर्वनाश राम ने किया।\nसत्यमेव जयते नानृतं सत्येन पन्था विततो देवयानः।\n0123456789\nThe quick brown fox jumps over the lazy dog.",
        ["ベンガル文字"] = "কালো মেঘের কোলে কোলে হাসছে সকাল বেলা।\nসকলের তরে সকলে আমরা, প্রত্যেকে আমরা পরের তরে।\n0123456789\nThe quick brown fox jumps over the lazy dog.",
        ["タミル文字"] = "எண்ணென்ப ஏனை எழுத்தென்ப இவ்விரண்டும்\nகண்ணென்ப வாழும் உயிர்க்கு.\n0123456789\nThe quick brown fox jumps over the lazy dog."
    };

    /// <summary>
    /// 指定された言語（または絵文字）に対応する代表文例テキストを取得します。
    /// 未登録言語の場合は標準フォールバックテキストを返します。
    /// </summary>
    public static string GetSampleTextForLanguage(string language)
    {
        if (LanguageSampleTexts.TryGetValue(language, out var sample))
        {
            return sample;
        }

        return $"[ {language} ]\nThe quick brown fox jumps over the lazy dog.\n0123456789";
    }
}
