import { getCompanyProfile, getMe, saveCompanyProfile } from '../api/assessments.js';
import { describeError, el, formatDate, requireElement } from '../components/dom.js';
import { setAsideLabels } from '../labels.js';
import type { CompanyFitProfile, CompanyProfile, SetAsideCategory } from '../models/api.js';

const splitList = (value: string): string[] =>
  value
    .split(/[,\n]/)
    .map((v) => v.trim())
    .filter((v) => v.length > 0);

function fill(form: HTMLFormElement, profile: CompanyProfile): void {
  const set = (name: string, value: string): void => {
    const field = form.elements.namedItem(name);
    if (field instanceof HTMLInputElement || field instanceof HTMLTextAreaElement) field.value = value;
  };
  set('naicsCodes', profile.fit.naicsCodes.join(', '));
  set('preferredStates', profile.fit.preferredStates.join(', '));
  set('preferredAgencies', profile.fit.preferredAgencies.join('\n'));
  set('capabilityKeywords', profile.fit.capabilityKeywords.join(', '));
  set('minimumResponseDays', String(profile.fit.minimumResponseDays));
  for (const box of form.querySelectorAll<HTMLInputElement>('input[name="setAsideQualifications"]')) {
    box.checked = profile.fit.setAsideQualifications.includes(box.value as SetAsideCategory);
  }
}

function read(form: HTMLFormElement): CompanyFitProfile {
  const data = new FormData(form);
  const text = (name: string): string => String(data.get(name) ?? '');
  return {
    naicsCodes: splitList(text('naicsCodes')),
    setAsideQualifications: data.getAll('setAsideQualifications').map((v) => String(v) as SetAsideCategory),
    preferredStates: splitList(text('preferredStates')).map((s) => s.toUpperCase()),
    preferredAgencies: splitList(text('preferredAgencies')),
    capabilityKeywords: splitList(text('capabilityKeywords')),
    minimumResponseDays: Number.parseInt(text('minimumResponseDays'), 10) || 0,
  };
}

export async function initCompanyPage(): Promise<void> {
  const form = requireElement('#profile-form', HTMLFormElement);
  const status = requireElement('#profile-status', HTMLElement);
  const updated = requireElement('#profile-updated', HTMLElement);
  const programs = requireElement('#set-aside-options', HTMLElement);

  for (const [value, label] of Object.entries(setAsideLabels)) {
    programs.append(el('label', { className: 'check' }, [el('input', { attrs: { type: 'checkbox', name: 'setAsideQualifications', value } }), ` ${label}`]));
  }

  const show = (profile: CompanyProfile): void => {
    fill(form, profile);
    updated.textContent = profile.updatedAt ? `Last updated ${formatDate(profile.updatedAt)} by ${profile.updatedBy ?? 'a former member'}.` : 'No profile has been saved yet. Opportunities cannot be scored until you add at least one detail.';
  };

  try {
    const [me, profile] = await Promise.all([getMe(), getCompanyProfile()]);
    requireElement('#company-name', HTMLElement).textContent = me.companyName ?? 'Your company';
    show(profile);
  } catch (error) {
    status.textContent = describeError(error);
  }

  form.addEventListener('submit', (event) => {
    event.preventDefault();
    void (async () => {
      try {
        show(await saveCompanyProfile(read(form)));
        status.textContent = 'Company profile saved.';
      } catch (error) {
        const reason = (error as { reasonCode?: string }).reasonCode;
        status.textContent = reason === 'role.insufficient' ? 'Only a team manager or company administrator can change the company profile.' : describeError(error);
      }
    })();
  });

  const extra = document.querySelector<HTMLElement>('#company-extra');
  if (extra) {
    const module = await import('./companyMembers.js').catch(() => null);
    if (module) await module.renderMembers(extra);
  }
}
