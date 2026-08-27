using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Orchestration;

public static class PhaseDomVerifier
{
    public static async Task<Dictionary<string, object>> VerifyAsync(string phase, CdpClient client, DesiredState desired)
    {
        var result = new Dictionary<string, object>();
        switch (phase)
        {
            case "availability":
                var availDom = await client.EvaluateAsync<Dictionary<string, object>>("""
                (() => {
                    const priceSelect = document.querySelector('he-select[name="pricingTier"], #pricingTier, [data-bi-name="PriceTierSelect"]');
                    const priceText = priceSelect?.innerText?.trim() || '';
                    const text = document.body?.innerText || '';
                    return {
                        priceText: priceText,
                        hasFree: priceText.includes('Free') || priceText.includes('免费') || priceText.includes('0.00'),
                        hasAllMarkets: text.includes('所有可能的市场') || text.includes('All possible markets') || text.includes('全部市场') || text.includes('已选市场')
                    };
                })()
                """);
                if (availDom != null) foreach (var kv in availDom) result[kv.Key] = kv.Value;
                break;

            case "properties":
                var propDom = await client.EvaluateAsync<Dictionary<string, object>>("""
                (() => {
                    const categorySelect = document.querySelector('he-select[name="category"], #category-select, [data-bi-name="CategorySelect"]');
                    const categoryText = categorySelect?.innerText?.trim() || '';
                    const text = document.body?.innerText || '';
                    const privacyNoRadio = document.querySelector('input[type="radio"][value="No"], input[name="privacy"][value="No"]');
                    const privacyTextarea = document.querySelector('#privacyPolicyText, textarea[name="privacyPolicyText"]');
                    return {
                        categoryText: categoryText,
                        isProductivity: categoryText.includes('Productivity') || categoryText.includes('生产力') || categoryText.includes('效率'),
                        privacyNoSelected: !!privacyNoRadio?.checked || text.includes('否，本产品不收集') || text.includes('No, this product does not'),
                        privacyTextareaExists: !!privacyTextarea
                    };
                })()
                """);
                if (propDom != null) foreach (var kv in propDom) result[kv.Key] = kv.Value;
                break;

            case "ageRatings":
                var ageDom = await client.EvaluateAsync<Dictionary<string, object>>("""
                (() => {
                    const text = document.body?.innerText || '';
                    const checkedRadios = Array.from(document.querySelectorAll('input[type="radio"]:checked')).map(r => r.value);
                    const isQuestionnaireComplete = checkedRadios.length >= 9 || text.includes('问卷已完成') || text.includes('Questionnaire completed') || text.includes('问卷调查完成');
                    const iarcCheckbox = document.querySelector('input[type="checkbox"]#iarc-declaration, input[type="checkbox"][name="iarcConsent"]');
                    return {
                        checkedRadiosCount: checkedRadios.length,
                        isQuestionnaireComplete: isQuestionnaireComplete,
                        iarcConsentChecked: !!iarcCheckbox?.checked || text.includes('已同意 IARC')
                    };
                })()
                """);
                if (ageDom != null) foreach (var kv in ageDom) result[kv.Key] = kv.Value;
                break;

            case "packages":
                var pkgDom = await client.EvaluateAsync<Dictionary<string, object>>("""
                (() => {
                    const rows = Array.from(document.querySelectorAll('tr, .package-row, [data-bi-area="PackageTable"] tr')).map(r => r.innerText?.trim() || '');
                    const hasValidated = rows.some(r => r.includes('Validated') || r.includes('已验证') || r.includes('已完成'));
                    const hasAnalyzing = rows.some(r => r.includes('Analyzing') || r.includes('正在分析') || r.includes('上传中'));
                    const hasError = rows.some(r => r.includes('Error') || r.includes('错误') || r.includes('失败'));
                    const text = document.body?.innerText || '';
                    return {
                        rowCount: rows.length,
                        hasValidated: hasValidated || text.includes('Validated') || text.includes('已验证'),
                        hasAnalyzing: hasAnalyzing,
                        hasError: hasError
                    };
                })()
                """);
                if (pkgDom != null) foreach (var kv in pkgDom) result[kv.Key] = kv.Value;
                break;

            case "listing":
                var listingDom = await client.EvaluateAsync<Dictionary<string, object>>("""
                (() => {
                    const descInput = document.querySelector('textarea#description, textarea[name="description"]');
                    const shortDescInput = document.querySelector('textarea#shortDescription, textarea[name="shortDescription"], input#shortDescription');
                    const imgTags = Array.from(document.querySelectorAll('.screenshot-preview img, .asset-thumbnail img, img[alt*="screenshot"], img[alt*="logo"]'));
                    const text = document.body?.innerText || '';
                    return {
                        descriptionLength: descInput ? (descInput.value || '').length : 0,
                        shortDescriptionLength: shortDescInput ? (shortDescInput.value || '').length : 0,
                        renderedImageCount: imgTags.length,
                        hasFeatureItems: text.includes('特性') || text.includes('Features') || text.includes('打开有仪式感') || text.includes('深色皮面'),
                        hasKeywords: text.includes('搜索词') || text.includes('Keywords') || text.includes('牵挂') || text.includes('纪念日')
                    };
                })()
                """);
                if (listingDom != null) foreach (var kv in listingDom) result[kv.Key] = kv.Value;
                break;

            case "options":
                var optDom = await client.EvaluateAsync<Dictionary<string, object>>("""
                (() => {
                    const runFullTrustText = document.querySelector('textarea#runFullTrustReason, textarea[name="runFullTrustReason"]')?.value || '';
                    const text = document.body?.innerText || '';
                    return {
                        hasRunFullTrustReason: runFullTrustText.length > 0 || text.includes('WinUI 3') || text.includes('全信任桌面进程'),
                        isPublishTimingSelected: text.includes('尽快发布') || text.includes('As soon as possible') || text.includes('手动发布')
                    };
                })()
                """);
                if (optDom != null) foreach (var kv in optDom) result[kv.Key] = kv.Value;
                break;
        }
        return result;
    }
}
