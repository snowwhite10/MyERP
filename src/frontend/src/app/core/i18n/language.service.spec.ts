import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { TranslateService } from '@ngx-translate/core';
import { en_US, NzI18nService, zh_CN } from 'ng-zorro-antd/i18n';
import { firstValueFrom } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PreferenceStorage } from '../storage/preference-storage';
import { LANGUAGE_STORAGE_KEY, LanguageService } from './language.service';

describe('LanguageService', () => {
  function setup(browserLanguages: string[] = ['zh-TW']) {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(browserLanguages);
    TestBed.configureTestingModule({ providers: provideTestTranslations() });
    return {
      service: TestBed.inject(LanguageService),
      translate: TestBed.inject(TranslateService),
      nzI18n: TestBed.inject(NzI18nService),
      storage: TestBed.inject(PreferenceStorage),
      document: TestBed.inject(DOCUMENT),
    };
  }

  beforeEach(() => localStorage.clear());
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('switches translations, ng-zorro locale, <html lang> and stored preference', async () => {
    const { service, translate, nzI18n, storage, document } = setup();

    await firstValueFrom(service.use('en'));

    expect(service.current()).toBe('en');
    expect(translate.getCurrentLang()).toBe('en');
    expect(translate.instant('common.action.save')).toBe('Save');
    expect(nzI18n.getLocale()).toBe(en_US);
    expect(document.documentElement.lang).toBe('en');
    expect(storage.get(LANGUAGE_STORAGE_KEY)).toBe('en');
  });

  it('switches to simplified Chinese', async () => {
    const { service, translate, nzI18n } = setup();

    await firstValueFrom(service.use('zh-Hans'));

    expect(translate.instant('common.action.save')).toBe('保存');
    expect(nzI18n.getLocale()).toBe(zh_CN);
  });

  it('init prefers the stored language', async () => {
    const { service, storage } = setup(['en-US']);
    storage.set(LANGUAGE_STORAGE_KEY, 'zh-Hans');

    await firstValueFrom(service.init());

    expect(service.current()).toBe('zh-Hans');
  });

  it('init falls back to the browser language when nothing valid is stored', async () => {
    const { service, storage } = setup(['en-GB']);
    storage.set(LANGUAGE_STORAGE_KEY, 'klingon');

    await firstValueFrom(service.init());

    expect(service.current()).toBe('en');
  });

  it('init sets zh-Hant as fallback language', async () => {
    const { service, translate } = setup(['ja']);

    await firstValueFrom(service.init());

    expect(service.current()).toBe('zh-Hant');
    expect(translate.getFallbackLang()).toBe('zh-Hant');
  });

  it('ignores unsupported codes and uses the default language', async () => {
    const { service } = setup();

    await firstValueFrom(service.use('fr' as never));

    expect(service.current()).toBe('zh-Hant');
  });
});
