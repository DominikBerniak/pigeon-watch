import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ErrorReference } from './error-reference';

@Component({
  imports: [ErrorReference],
  template: `<app-error-reference [traceId]="traceId()" />`,
})
class ErrorReferenceHost {
  readonly traceId = signal<string | null | undefined>(null);
}

describe('ErrorReference', () => {
  async function render(traceId: string | null | undefined): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    const fixture = TestBed.createComponent(ErrorReferenceHost);
    fixture.componentInstance.traceId.set(traceId);
    await fixture.whenStable();

    return fixture.nativeElement as HTMLElement;
  }

  it('renders the reference with the id when one is given', async () => {
    const root = await render('00-abc123-def456-01');

    expect(root.querySelector('app-error-reference')?.textContent?.trim()).toBe(
      'Reference: 00-abc123-def456-01',
    );
  });

  it.each([null, undefined, ''])('renders nothing when the id is %s', async (traceId) => {
    const root = await render(traceId);

    expect(root.querySelector('app-error-reference')?.textContent?.trim()).toBe('');
    expect(root.querySelector('app-error-reference')?.children.length).toBe(0);
  });
});
