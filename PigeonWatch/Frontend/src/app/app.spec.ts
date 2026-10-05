import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { SessionService } from './core/auth/session.service';
import { CurrentUser } from './core/configuration/configuration.service';

describe('App', () => {
  const currentUser = signal<CurrentUser | null>(null);
  const logout = vi.fn();

  beforeEach(async () => {
    currentUser.set(null);
    logout.mockReset();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: SessionService, useValue: { currentUser: currentUser.asReadonly(), logout } },
      ],
    }).compileComponents();
  });

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    return fixture.nativeElement;
  }

  it('renders the warm-up panel above the router outlet', async () => {
    const element = await render();
    const panel = element.querySelector('app-warmup-panel');
    const outlet = element.querySelector('main router-outlet');

    expect(panel).not.toBeNull();
    expect(outlet).not.toBeNull();
    expect(panel?.compareDocumentPosition(outlet as Node)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
  });

  it('hides the header when nobody is logged in', async () => {
    const element = await render();

    expect(element.querySelector('header')).toBeNull();
  });

  it('shows who is logged in and logs out', async () => {
    currentUser.set({ id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] });
    const element = await render();

    expect(element.querySelector('header')?.textContent).toContain('Logged in as Jan K');
    expect(element.querySelector('header')?.textContent).not.toContain('jan@example.com');
    const button = element.querySelector<HTMLButtonElement>('header button');
    expect(button?.textContent?.trim()).toBe('Log out');

    button?.click();

    expect(logout).toHaveBeenCalledOnce();
  });
});
