import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Brand, BrandSize } from './brand';

@Component({
  imports: [Brand],
  template: `<app-brand [name]="'PigeonWatch'" [size]="size()" />`,
})
class BrandHost {
  readonly size = signal<BrandSize>('md');
}

describe('Brand', () => {
  it('renders the passed name next to a decorative mark', async () => {
    const fixture = TestBed.createComponent(BrandHost);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('.brand__name')?.textContent?.trim()).toBe('PigeonWatch');
    expect(element.querySelector('.brand__mark')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('switches to the large variant through the size input', async () => {
    const fixture = TestBed.createComponent(BrandHost);
    await fixture.whenStable();
    const brand: HTMLElement = fixture.nativeElement.querySelector('app-brand');

    expect(brand.classList.contains('brand--lg')).toBe(false);

    fixture.componentInstance.size.set('lg');
    await fixture.whenStable();

    expect(brand.classList.contains('brand--lg')).toBe(true);
  });
});
