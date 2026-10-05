import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { WarmupState } from '../warmup-state';
import { WarmupPanel, warmupMessageIntervalMs } from './warmup-panel';

const firstMessage =
  "Congratulations! You're the first rescuer today. Wait for the application to load.";
const secondMessage = 'Our server was napping on a warm windowsill. Waking it up…';

describe('WarmupPanel', () => {
  let fixture: ComponentFixture<WarmupPanel>;
  let state: WarmupState;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    state = TestBed.inject(WarmupState);
    fixture = TestBed.createComponent(WarmupPanel);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function panel(): HTMLElement {
    return fixture.nativeElement.querySelector('[role="status"]');
  }

  it('renders an empty live region while idle', async () => {
    await fixture.whenStable();

    expect(panel().getAttribute('aria-live')).toBe('polite');
    expect(panel().classList).not.toContain('warmup-panel--visible');
    expect(panel().textContent?.trim()).toBe('');
  });

  it('shows the first message while warming and rotates the messages', () => {
    vi.useFakeTimers();
    state.begin();
    fixture.detectChanges();

    expect(panel().classList).toContain('warmup-panel--visible');
    expect(panel().querySelector('h2')?.textContent?.trim()).toBe('Waking up PigeonWatch');
    expect(panel().querySelector('p')?.textContent?.trim()).toBe(firstMessage);

    vi.advanceTimersByTime(warmupMessageIntervalMs);
    fixture.detectChanges();

    expect(panel().querySelector('p')?.textContent?.trim()).toBe(secondMessage);
  });

  it('shows Try again when failed and dismisses a warm-up that began after startup', async () => {
    state.markStartupComplete();
    state.begin();
    state.end('failed');
    await fixture.whenStable();

    expect(panel().querySelector('app-page-card h1')?.textContent?.trim()).toBe(
      'PigeonWatch is taking its time',
    );
    expect(panel().textContent).toContain('Still asleep. Give it another minute.');
    const button = panel().querySelector<HTMLButtonElement>('button');
    expect(button?.textContent?.trim()).toBe('Try again');

    button?.click();
    await fixture.whenStable();

    expect(state.status()).toBe('idle');
    expect(panel().classList).not.toContain('warmup-panel--visible');
  });
});
