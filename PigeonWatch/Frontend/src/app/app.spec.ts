import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, Routes, provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';
import { SessionService } from './core/auth/session.service';
import { CurrentUser } from './core/configuration/configuration.service';

@Component({ template: '' })
class BlankPage {}

const testRoutes: Routes = [
  { path: '', pathMatch: 'full', component: BlankPage, data: { layout: 'wide' } },
  { path: 'profile', component: BlankPage },
];

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
        provideRouter(testRoutes),
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

  async function openUserMenu(): Promise<HTMLElement> {
    currentUser.set({ id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] });
    const element = await render();
    const trigger = element.querySelector<HTMLButtonElement>('header button');

    trigger?.click();
    await TestBed.inject(ApplicationRef).whenStable();

    return element;
  }

  function menuItems(): HTMLElement[] {
    return Array.from(document.body.querySelectorAll<HTMLElement>('[role="menuitem"]'));
  }

  it('shows the user name as the menu button without the email', async () => {
    currentUser.set({ id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] });
    const element = await render();
    const header = element.querySelector('header');

    expect(header?.querySelector('button')?.textContent?.trim()).toBe('Jan K');
    expect(header?.textContent).not.toContain('jan@example.com');
    expect(header?.textContent).not.toContain('Logged in as');
    expect(menuItems()).toEqual([]);
  });

  it('opens a menu with Report sighting, Profile and Log out', async () => {
    await openUserMenu();

    const items = menuItems();

    expect(items.map((item) => item.textContent?.trim())).toEqual([
      'Report sighting',
      'Profile',
      'Log out',
    ]);
    expect(items[0].getAttribute('href')).toBe('/sightings/new');
    expect(items[1].getAttribute('href')).toBe('/profile');
  });

  it('logs out from the menu', async () => {
    await openUserMenu();

    menuItems()[2].click();

    expect(logout).toHaveBeenCalledOnce();
  });

  it('widens main only on a route whose data asks for the wide layout', async () => {
    currentUser.set({ id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] });
    const element = await render();
    const router = TestBed.inject(Router);
    const main = element.querySelector('main')!;

    await router.navigateByUrl('/');
    await TestBed.inject(ApplicationRef).whenStable();

    expect(main.classList).toContain('app-main--wide');

    await router.navigateByUrl('/profile');
    await TestBed.inject(ApplicationRef).whenStable();

    expect(main.classList).not.toContain('app-main--wide');
  });

  it('declares the wide layout on the home route only', () => {
    const wide = routes.filter((route) => route.data?.['layout'] === 'wide');

    expect(wide.map((route) => route.path)).toEqual(['']);
  });
});
