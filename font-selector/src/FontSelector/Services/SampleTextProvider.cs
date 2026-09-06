using System;
using System.Collections.Generic;

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
        "☕♨✈✉❄☀☁☂⚽⛄🐱🐶🍎🚗🚀🏠🎉🔥\n" +
        "🤗🤔🤖🌮🍿🍾🦄🐿🦀🦁🧀🏹👋👋🏻👋🏼👋🏽👋🏾👋🏿\n" +
        "🤣🤤🤢🤧🤠🤡🤥🥞🥓🥐🥑🦆🦇🦉🦊🦋🦖🥩\n" +
        "🥰🥵🥶🥳🥺🦹🦝🦙🦛🧁🧂🦮🦯🦼🦽🪐🦩🦥\n" +
        "🥲🥸🤌🫀🫁🥷🦤🦭🫠🫢🫣🫡🫥🫦🫧🫶🫘\n" +
        "🫨🩷🪿🧑‍💻🍋‍🟩🙂‍↕️🧑‍🧑‍🧒👩‍❤️‍👨👨‍👩‍👧‍👦👩‍❤️‍💋‍👨🏳️‍🌈🫪🫯🫍🫈🪊🛘🪎";

    private static readonly Dictionary<string, string> LanguageSampleTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["日本語"] = "𠮷野の山に ふりつむ雪の 𣘺を渡れば、\n𛀵るの野に 鶯鳴きて 木の芽の雫 𛁞にほひけり。\nABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789\nThe quick brown fox jumps over the lazy dog.\ni1llI|! oοｏ0OＯ rn m vv w S 5 $ B 8 Z 2\nー ― - － 一 ソン シツ 口ロ 生𛁛𛂦゙ う𛂂ぎ",
        ["英語 / ラテン文字"] = "L’officiel français préfère l’efficacité, le cœur & la flûte.\n" +
                              "Überflüssige Konflikte stören die süße Musik & das Æther.\n" +
                              "fi fl ff ffi ffl ft st ct | == != <= >= => -> --> <-> := <!--\n" +
                              "Åå Øø Ææ Œœ Þþ Ðð Çç Łł Šš Žž Ññ ĳ ß ẞ ¿¡\n" +
                              "0123456789 0OØ ½ ¼ ¾ ¹²³ $ € £ ¥ ₹ ₽ — – “curly” ‘quotes’ «»",
        ["英語 / ラテン文字 (私用領域・Symbol)"] = "L’officiel français préfère l’efficacité, le cœur & la flûte.\n" +
                                               "Überflüssige Konflikte stören die süße Musik & das Æther.\n" +
                                               "fi fl ff ffi ffl ft st ct | == != <= >= => -> --> <-> := <!--\n" +
                                               "Åå Øø Ææ Œœ Þþ Ðð Çç Łł Šš Žž Ññ ĳ ß ẞ ¿¡\n" +
                                               "0123456789 0OØ ½ ¼ ¾ ¹²³ $ € £ ¥ ₹ ₽ — – “curly” ‘quotes’ «»",
        ["韓国語"] = "나랏말이 중국과 달라 한자와는 서로 통하지 아니하여,\n백성이 말하고자 하는 バ가 있어도 뜻을 펴지 못한다.\n0123456789\nThe quick brown fox jumps over the lazy dog.",
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

    public static (string text, double size) GetSampleTextForLanguage(string language)
    {
        if (LanguageSampleTexts.TryGetValue(language, out var sample))
        {
            return (sample, 22.0);
        }

        return ($"[ {language} ]\nThe quick brown fox jumps over the lazy dog.\n0123456789", 22.0);
    }

    public static (string text, double size) GetEmojiSampleText()
    {
        return (EmojiSampleText, 22.0);
    }

    public const string NerdFontsSampleText =
        "\uE0B6\uF120 dev \uE0B0 \uF07C ~/src \uE0B1 \uE0A0 main \uF00C \uE0B4 \uE73C 3.12 \uF017 2s \u276F\n" +
        "X\uF1D3X\uF113X\uF017X\uF219X\uF015X\uF07CX\uF115X\uF0E4X\uF084\uF0F3\uF02B\uF0E7\uF0AD\uF135\uF2DC\uF0C2\uF080\uF06E\uF02D\uF0E0\uF073\uF024\uF0C3\uF028\uF1EB\uF188\uF06D\n" +
        "\uF306\uF31B\uF303\uF30C\uF17C\uF300\uF314\uF179\uF17A\uF17B\uF313\uF312\uF308\uF31A\uF1D3\uF09B\uF296\uF126\uE729\uF407\uE725\uF1D2\uF00C\uF00D\uF071\uF05A\uF004\uF005\uF118\uF058\uF057\uF059\uF06A\n" +
        "\uE73C \uE7A8 \uE627 \uE61D \uE781 \uE628 \uE7B0 \uF1C0 | \uF07B \uF07C \uF15B \uF15C \uF121 \uF013 \uF002 \uF023 \uF0C7 \uF120";

    public static (string text, double size) GetNerdFontsSampleText()
    {
        return (NerdFontsSampleText, 22.0);
    }
}
