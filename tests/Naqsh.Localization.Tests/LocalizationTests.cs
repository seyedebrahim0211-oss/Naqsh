using System.Globalization;
using Naqsh.Core.Settings;

namespace Naqsh.Localization.Tests;

public class ResourceTests
{
    private static readonly CultureInfo Fa = CultureInfo.GetCultureInfo("fa-IR");

    [Fact]
    public void EveryKey_ExistsInBothLanguages()
    {
        var en = LocalizationService.AllKeys();
        var fa = LocalizationService.KeysFor(Fa);
        Assert.NotEmpty(en);
        Assert.Equal(en, fa);
    }

    [Fact]
    public void NoValue_IsEmpty()
    {
        foreach (var key in LocalizationService.AllKeys())
        {
            Assert.False(string.IsNullOrWhiteSpace(LocalizationService.ValueFor(key, CultureInfo.InvariantCulture)), key);
            Assert.False(string.IsNullOrWhiteSpace(LocalizationService.ValueFor(key, Fa)), key);
        }
    }

    [Fact]
    public void FormatPlaceholders_MatchBetweenLanguages()
    {
        foreach (var key in LocalizationService.AllKeys())
        {
            var en = LocalizationService.ValueFor(key, CultureInfo.InvariantCulture)!;
            var fa = LocalizationService.ValueFor(key, Fa)!;
            for (var i = 0; i < 4; i++)
            {
                var p = "{" + i.ToString(CultureInfo.InvariantCulture) + "}";
                Assert.True(en.Contains(p, StringComparison.Ordinal) == fa.Contains(p, StringComparison.Ordinal), $"{key}: placeholder {p}");
            }
        }
    }

    [Fact]
    public void PersianValues_UsePersianLetters_NotArabicYehKaf()
    {
        // Arabic ي (U+064A) and ك (U+0643) look similar but break search and shaping in Persian.
        foreach (var key in LocalizationService.AllKeys())
        {
            var fa = LocalizationService.ValueFor(key, Fa)!;
            Assert.DoesNotContain('ي', fa);
            Assert.DoesNotContain('ك', fa);
        }
    }

    [Fact]
    public void Persian_IsSuppliedFromSatelliteAssembly()
    {
        Assert.Equal("پیش‌نمایش", LocalizationService.ValueFor("Monitor_Preview", Fa));
        Assert.Equal("PREVIEW", LocalizationService.ValueFor("Monitor_Preview", CultureInfo.InvariantCulture));
    }
}

[Collection("LocalizationService")]
public class ServiceTests
{
    [Fact]
    public void SwitchingLanguage_RaisesChange_AndFlipsDirection()
    {
        var loc = LocalizationService.Instance;
        loc.SetFormatting(DigitStyle.FollowLanguage, CalendarStyle.FollowLanguage, true);
        loc.SetLanguage(AppSettings.Persian);
        var raised = new List<string?>();
        loc.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        try
        {
            loc.SetLanguage(AppSettings.English);
            Assert.False(loc.IsRightToLeft);
            Assert.Equal("CUT", loc["Transition_Cut"]);
            Assert.Contains("Item[]", raised);

            loc.SetLanguage(AppSettings.Persian);
            Assert.True(loc.IsRightToLeft);
            Assert.Equal("برش", loc["Transition_Cut"]);
        }
        finally
        {
            loc.SetLanguage(AppSettings.Persian);
        }
    }

    [Fact]
    public void MissingKey_IsVisible_AndReported()
    {
        string? reported = null;
        LocalizationService.MissingKeyReporter = k => reported = k;
        try
        {
            Assert.Equal("[No_Such_Key]", LocalizationService.Instance.Get("No_Such_Key"));
            Assert.Equal("No_Such_Key", reported);
        }
        finally
        {
            LocalizationService.MissingKeyReporter = null;
        }
    }

    [Fact]
    public void Format_UsesPersianDigits_InPersian()
    {
        var loc = LocalizationService.Instance;
        loc.SetLanguage(AppSettings.Persian);
        loc.SetFormatting(DigitStyle.FollowLanguage, CalendarStyle.FollowLanguage, true);
        Assert.Equal("افت فریم: ۱۲", loc.Format("Status_Dropped", 12));
        loc.SetFormatting(DigitStyle.Latin, CalendarStyle.FollowLanguage, true);
        Assert.Equal("افت فریم: 12", loc.Format("Status_Dropped", 12));
        loc.SetFormatting(DigitStyle.FollowLanguage, CalendarStyle.FollowLanguage, true);
    }
}

public class NumberFormatterTests
{
    [Theory]
    [InlineData("1405", "۱۴۰۵")]
    [InlineData("12:30:05", "۱۲:۳۰:۰۵")]
    [InlineData("59.94", "۵۹٫۹۴")]
    [InlineData("Live 2026.", "Live ۲۰۲۶.")]
    public void ToPersianDigits(string latin, string persian)
    {
        Assert.Equal(persian, NumberFormatter.ToPersianDigits(latin));
    }

    [Fact]
    public void ToLatinDigits_HandlesPersianAndArabicIndic()
    {
        Assert.Equal("1405", NumberFormatter.ToLatinDigits("۱۴۰۵"));
        Assert.Equal("123", NumberFormatter.ToLatinDigits("١٢٣"));
        Assert.Equal("59.94", NumberFormatter.ToLatinDigits("۵۹٫۹۴"));
    }
}

[Collection("LocalizationService")]
public class DateFormatterTests
{
    [Theory]
    [InlineData(2026, 3, 21, 1405, 1, 1)] // Nowruz 1405
    [InlineData(2026, 11, 1, 1405, 8, 10)]
    [InlineData(2025, 3, 20, 1403, 12, 30)] // leap Esfand 30
    [InlineData(2024, 1, 1, 1402, 10, 11)]
    public void Jalali_Conversion(int gy, int gm, int gd, int jy, int jm, int jd)
    {
        var j = DateFormatter.ToJalali(new DateTime(gy, gm, gd));
        Assert.Equal(new JalaliDate(jy, jm, jd), j);
        Assert.Equal(new DateTime(gy, gm, gd), DateFormatter.FromJalali(j));
    }

    [Fact]
    public void FormatDate_PersianJalali_And_EnglishGregorian()
    {
        var loc = LocalizationService.Instance;
        try
        {
            loc.SetLanguage(AppSettings.Persian);
            loc.SetFormatting(DigitStyle.FollowLanguage, CalendarStyle.FollowLanguage, true);
            Assert.Equal("یکشنبه ۱۰ آبان ۱۴۰۵", DateFormatter.FormatDate(new DateTime(2026, 11, 1), loc));

            loc.SetLanguage(AppSettings.English);
            Assert.Equal("Sunday 1 November 2026", DateFormatter.FormatDate(new DateTime(2026, 11, 1), loc));

            // Independent settings: English UI with Jalali calendar.
            loc.SetFormatting(DigitStyle.FollowLanguage, CalendarStyle.Jalali, true);
            Assert.Equal("Sunday 10 Aban 1405", DateFormatter.FormatDate(new DateTime(2026, 11, 1), loc));
        }
        finally
        {
            loc.SetLanguage(AppSettings.Persian);
            loc.SetFormatting(DigitStyle.FollowLanguage, CalendarStyle.FollowLanguage, true);
        }
    }

    [Fact]
    public void FormatTime_24hAnd12h()
    {
        var loc = LocalizationService.Instance;
        try
        {
            loc.SetLanguage(AppSettings.English);
            loc.SetFormatting(DigitStyle.FollowLanguage, CalendarStyle.FollowLanguage, true);
            Assert.Equal("21:05:09", DateFormatter.FormatTime(new DateTime(2026, 1, 1, 21, 5, 9), loc));
            loc.SetFormatting(DigitStyle.FollowLanguage, CalendarStyle.FollowLanguage, false);
            Assert.Equal("9:05:09 PM", DateFormatter.FormatTime(new DateTime(2026, 1, 1, 21, 5, 9), loc));
            loc.SetLanguage(AppSettings.Persian);
            loc.SetFormatting(DigitStyle.FollowLanguage, CalendarStyle.FollowLanguage, true);
            Assert.Equal("۲۱:۰۵:۰۹", DateFormatter.FormatTime(new DateTime(2026, 1, 1, 21, 5, 9), loc));
        }
        finally
        {
            loc.SetLanguage(AppSettings.Persian);
            loc.SetFormatting(DigitStyle.FollowLanguage, CalendarStyle.FollowLanguage, true);
        }
    }
}

[CollectionDefinition("LocalizationService", DisableParallelization = true)]
public class LocalizationServiceGroup
{
}
