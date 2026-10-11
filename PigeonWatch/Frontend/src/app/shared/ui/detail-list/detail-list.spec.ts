import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DetailItem } from './detail-item';
import { DetailList, DetailListLayout } from './detail-list';

@Component({
  imports: [DetailList, DetailItem],
  template: `<dl app-detail-list [layout]="layout()">
    <div app-detail-item label="Status">Caught</div>
    <div app-detail-item label="Location">52.1, 19.5</div>
  </dl>`,
})
class DetailListHost {
  readonly layout = signal<DetailListLayout>('column');
}

describe('DetailList', () => {
  async function render(layout: DetailListLayout): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(DetailListHost);
    fixture.componentInstance.layout.set(layout);
    await fixture.whenStable();

    return fixture.nativeElement.querySelector('dl');
  }

  it('renders valid dl > div > dt + dd markup with the projected items in order', async () => {
    const list = await render('column');

    expect(
      [...list.querySelectorAll(':scope > div')].map((row) => [
        row.querySelector(':scope > dt')?.textContent?.trim(),
        row.querySelector(':scope > dt + dd')?.textContent?.trim(),
      ]),
    ).toEqual([
      ['Status', 'Caught'],
      ['Location', '52.1, 19.5'],
    ]);
  });

  it('stacks the rows in the column layout', async () => {
    const list = await render('column');
    const style = getComputedStyle(list);

    expect(list.classList).not.toContain('detail-list--inline');
    expect(style.display).toBe('flex');
    expect(style.flexDirection).toBe('column');
  });

  it('wraps the rows side by side in the inline layout', async () => {
    const list = await render('inline');
    const style = getComputedStyle(list);

    expect(list.classList).toContain('detail-list--inline');
    expect(style.flexDirection).toBe('row');
    expect(style.flexWrap).toBe('wrap');
  });
});
