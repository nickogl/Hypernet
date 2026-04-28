using System.Text;

namespace Hypernet;

internal static class Charset
{
	static Charset()
	{
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
	}

	public static bool TryGetEncoding(ReadOnlySpan<char> charset, out Encoding? encoding)
	{
		if (!_codePageByCharsetSpan.TryGetValue(charset, out var codePage))
		{
			encoding = null;
			return false;
		}

		try
		{
			encoding = Encoding.GetEncoding(codePage);
			return true;
		}
		catch (ArgumentException)
		{
			encoding = null;
			return false;
		}
	}

	private static readonly Dictionary<string, int> _codePageByCharset = new(StringComparer.OrdinalIgnoreCase)
	{
		// UTF-8
		["unicode-1-1-utf-8"] = 65001,
		["unicode11utf8"] = 65001,
		["unicode20utf8"] = 65001,
		["utf-8"] = 65001,
		["utf8"] = 65001,
		["x-unicode20utf8"] = 65001,

		// UTF-16BE
		["unicodefffe"] = 1201,
		["utf-16be"] = 1201,

		// UTF-16LE
		["csunicode"] = 1200,
		["iso-10646-ucs-2"] = 1200,
		["ucs-2"] = 1200,
		["unicode"] = 1200,
		["unicodefeff"] = 1200,
		["utf-16"] = 1200,
		["utf-16le"] = 1200,

		// IBM866
		["866"] = 866,
		["cp866"] = 866,
		["csibm866"] = 866,
		["ibm866"] = 866,

		// ISO-8859-2
		["csisolatin2"] = 28592,
		["iso-8859-2"] = 28592,
		["iso-ir-101"] = 28592,
		["iso8859-2"] = 28592,
		["iso88592"] = 28592,
		["iso_8859-2"] = 28592,
		["iso_8859-2:1987"] = 28592,
		["l2"] = 28592,
		["latin2"] = 28592,

		// ISO-8859-3
		["csisolatin3"] = 28593,
		["iso-8859-3"] = 28593,
		["iso-ir-109"] = 28593,
		["iso8859-3"] = 28593,
		["iso88593"] = 28593,
		["iso_8859-3"] = 28593,
		["iso_8859-3:1988"] = 28593,
		["l3"] = 28593,
		["latin3"] = 28593,

		// ISO-8859-4
		["csisolatin4"] = 28594,
		["iso-8859-4"] = 28594,
		["iso-ir-110"] = 28594,
		["iso8859-4"] = 28594,
		["iso88594"] = 28594,
		["iso_8859-4"] = 28594,
		["iso_8859-4:1988"] = 28594,
		["l4"] = 28594,
		["latin4"] = 28594,

		// ISO-8859-5
		["csisolatincyrillic"] = 28595,
		["cyrillic"] = 28595,
		["iso-8859-5"] = 28595,
		["iso-ir-144"] = 28595,
		["iso8859-5"] = 28595,
		["iso88595"] = 28595,
		["iso_8859-5"] = 28595,
		["iso_8859-5:1988"] = 28595,

		// ISO-8859-6
		["arabic"] = 28596,
		["asmo-708"] = 28596,
		["csiso88596e"] = 28596,
		["csiso88596i"] = 28596,
		["csisolatinarabic"] = 28596,
		["ecma-114"] = 28596,
		["iso-8859-6"] = 28596,
		["iso-8859-6-e"] = 28596,
		["iso-8859-6-i"] = 28596,
		["iso-ir-127"] = 28596,
		["iso8859-6"] = 28596,
		["iso88596"] = 28596,
		["iso_8859-6"] = 28596,
		["iso_8859-6:1987"] = 28596,

		// ISO-8859-7
		["csisolatingreek"] = 28597,
		["ecma-118"] = 28597,
		["elot_928"] = 28597,
		["greek"] = 28597,
		["greek8"] = 28597,
		["iso-8859-7"] = 28597,
		["iso-ir-126"] = 28597,
		["iso8859-7"] = 28597,
		["iso88597"] = 28597,
		["iso_8859-7"] = 28597,
		["iso_8859-7:1987"] = 28597,
		["sun_eu_greek"] = 28597,

		// ISO-8859-8
		["csiso88598e"] = 28598,
		["csisolatinhebrew"] = 28598,
		["hebrew"] = 28598,
		["iso-8859-8"] = 28598,
		["iso-8859-8-e"] = 28598,
		["iso-ir-138"] = 28598,
		["iso8859-8"] = 28598,
		["iso88598"] = 28598,
		["iso_8859-8"] = 28598,
		["iso_8859-8:1988"] = 28598,
		["visual"] = 28598,

		// ISO-8859-8-I
		["csiso88598i"] = 28598,
		["iso-8859-8-i"] = 28598,
		["logical"] = 28598,

		// ISO-8859-10
		["csisolatin6"] = 28600,
		["iso-8859-10"] = 28600,
		["iso-ir-157"] = 28600,
		["iso8859-10"] = 28600,
		["iso885910"] = 28600,
		["l6"] = 28600,
		["latin6"] = 28600,

		// ISO-8859-13
		["iso-8859-13"] = 28603,
		["iso8859-13"] = 28603,
		["iso885913"] = 28603,

		// ISO-8859-14
		["iso-8859-14"] = 28604,
		["iso8859-14"] = 28604,
		["iso885914"] = 28604,

		// ISO-8859-15
		["csisolatin9"] = 28605,
		["iso-8859-15"] = 28605,
		["iso8859-15"] = 28605,
		["iso885915"] = 28605,
		["iso_8859-15"] = 28605,
		["l9"] = 28605,

		// ISO-8859-16
		["iso-8859-16"] = 28606,

		// KOI8
		["cskoi8r"] = 20866,
		["koi"] = 20866,
		["koi8"] = 20866,
		["koi8-r"] = 20866,
		["koi8_r"] = 20866,

		["koi8-ru"] = 21866,
		["koi8-u"] = 21866,

		// Macintosh
		["csmacintosh"] = 10000,
		["mac"] = 10000,
		["macintosh"] = 10000,
		["x-mac-roman"] = 10000,

		["x-mac-cyrillic"] = 10007,
		["x-mac-ukrainian"] = 10007,

		// windows-874
		["dos-874"] = 874,
		["iso-8859-11"] = 874,
		["iso8859-11"] = 874,
		["iso885911"] = 874,
		["tis-620"] = 874,
		["windows-874"] = 874,

		// windows-1250
		["cp1250"] = 1250,
		["windows-1250"] = 1250,
		["x-cp1250"] = 1250,

		// windows-1251
		["cp1251"] = 1251,
		["windows-1251"] = 1251,
		["x-cp1251"] = 1251,

		// windows-1252
		// WHATWG maps ISO-8859-1 and ASCII-looking labels to windows-1252.
		["ansi_x3.4-1968"] = 1252,
		["ascii"] = 1252,
		["cp1252"] = 1252,
		["cp819"] = 1252,
		["csisolatin1"] = 1252,
		["ibm819"] = 1252,
		["iso-8859-1"] = 1252,
		["iso-ir-100"] = 1252,
		["iso8859-1"] = 1252,
		["iso88591"] = 1252,
		["iso_8859-1"] = 1252,
		["iso_8859-1:1987"] = 1252,
		["l1"] = 1252,
		["latin1"] = 1252,
		["us-ascii"] = 1252,
		["windows-1252"] = 1252,
		["x-cp1252"] = 1252,

		// windows-1253
		["cp1253"] = 1253,
		["windows-1253"] = 1253,
		["x-cp1253"] = 1253,

		// windows-1254
		["cp1254"] = 1254,
		["csisolatin5"] = 1254,
		["iso-8859-9"] = 1254,
		["iso-ir-148"] = 1254,
		["iso8859-9"] = 1254,
		["iso88599"] = 1254,
		["iso_8859-9"] = 1254,
		["iso_8859-9:1989"] = 1254,
		["l5"] = 1254,
		["latin5"] = 1254,
		["windows-1254"] = 1254,
		["x-cp1254"] = 1254,

		// windows-1255
		["cp1255"] = 1255,
		["windows-1255"] = 1255,
		["x-cp1255"] = 1255,

		// windows-1256
		["cp1256"] = 1256,
		["windows-1256"] = 1256,
		["x-cp1256"] = 1256,

		// windows-1257
		["cp1257"] = 1257,
		["windows-1257"] = 1257,
		["x-cp1257"] = 1257,

		// windows-1258
		["cp1258"] = 1258,
		["windows-1258"] = 1258,
		["x-cp1258"] = 1258,

		// GBK
		["chinese"] = 936,
		["csgb2312"] = 936,
		["csiso58gb231280"] = 936,
		["gb2312"] = 936,
		["gb_2312"] = 936,
		["gb_2312-80"] = 936,
		["gbk"] = 936,
		["iso-ir-58"] = 936,
		["x-gbk"] = 936,

		// gb18030
		["gb18030"] = 54936,

		// Big5
		["big5"] = 950,
		["big5-hkscs"] = 950,
		["cn-big5"] = 950,
		["csbig5"] = 950,
		["x-x-big5"] = 950,

		// EUC-JP
		["cseucpkdfmtjapanese"] = 51932,
		["euc-jp"] = 51932,
		["x-euc-jp"] = 51932,

		// ISO-2022-JP
		["csiso2022jp"] = 50220,
		["iso-2022-jp"] = 50220,

		// Shift_JIS
		["csshiftjis"] = 932,
		["ms932"] = 932,
		["ms_kanji"] = 932,
		["shift-jis"] = 932,
		["shift_jis"] = 932,
		["sjis"] = 932,
		["windows-31j"] = 932,
		["x-sjis"] = 932,

		// EUC-KR
		["cseuckr"] = 51949,
		["csksc56011987"] = 51949,
		["euc-kr"] = 51949,
		["iso-ir-149"] = 51949,
		["korean"] = 51949,
		["ks_c_5601-1987"] = 51949,
		["ks_c_5601-1989"] = 51949,
		["ksc5601"] = 51949,
		["ksc_5601"] = 51949,
		["windows-949"] = 51949,
	};

	private static readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _codePageByCharsetSpan
		= _codePageByCharset.GetAlternateLookup<ReadOnlySpan<char>>();
}
