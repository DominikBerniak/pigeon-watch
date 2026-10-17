import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DetailItem, DetailItemValue } from './detail-item';
import { DetailList } from './detail-list';

@Component({
  imports: [DetailList, DetailItem],
  template: `<dl app-detail-list>
    <div app-detail-item label="Description" [value]="value()">Line one</div>
  </dl>`,
})
class DetailItemHost {
  readonly value = signal<DetailItemValue>('plain');
}

describe('DetailItem', () => {
  async function render(value: DetailItemValue): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(DetailItemHost);
    fixture.componentInstance.value.set(value);
    await fixture.whenStable();

    return fixture.nativeElement.querySelector('dl > div');
  }

  it('renders a muted, small label and the projected value', async () => {
    const item = await render('plain');
    const term = item.querySelector('dt');

    expect(term?.textContent?.trim()).toBe('Description');
    expect(term?.classList).toContain('pw-text-muted');
    expect(term?.classList).toContain('pw-text-small');
    expect(item.querySelector('dd')?.textContent?.trim()).toBe('Line one');
  });

  it('keeps the default whitespace and figures for a plain value', async () => {
    const value = (await render('plain')).querySelector('dd')!;

    expect(getComputedStyle(value).whiteSpace).not.toBe('pre-line');
    expect(getComputedStyle(value).fontVariantNumeric).not.toBe('tabular-nums');
  });

  it('preserves line breaks for a text value', async () => {
    const value = (await render('text')).querySelector('dd')!;

    expect(getComputedStyle(value).whiteSpace).toBe('pre-line');
  });

  it('uses tabular figures for a numeric value', async () => {
    const value = (await render('numeric')).querySelector('dd')!;

    expect(getComputedStyle(value).fontVariantNumeric).toBe('tabular-nums');
  });
});
