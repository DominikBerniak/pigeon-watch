import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, Routes, provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';
import { SessionService } from './core/auth/session.service';
import { CurrentUser } from './core/configuration/configuration.service';
import { devRoutes } from './dev/dev-routes';

@Component({ template: '' })
class BlankPage {}

const testRoutes: Routes = [
  { path: '', pathMatch: 'full', component: BlankPage, data: { layout: 'wide' } },
  { path: 'roomy', component: BlankPage, data: { layout: 'roomy' } },
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

  it('links the brand in the header to the home page', async () => {
    currentUser.set({ id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] });
    const element = await render();
    const link = element.querySelector<HTMLAnchorElement>('header a.app-header__home');

    expect(link?.getAttribute('href')).toBe('/');
    expect(link?.textContent?.trim()).toBe('PigeonWatch');
  });

  it('logs out from the menu', async () => {
    await openUserMenu();

    menuItems()[2].click();

    expect(logout).toHaveBeenCalledOnce();
  });

  async function renderSignedIn(): Promise<{ main: HTMLElement }> {
    currentUser.set({ id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] });
    const element = await render();

    return { main: element.querySelector('main')! };
  }

  async function navigate(url: string): Promise<void> {
    await TestBed.inject(Router).navigateByUrl(url);
    await TestBed.inject(ApplicationRef).whenStable();
  }

  it('widens main on a route whose data asks for the wide layout', async () => {
    const { main } = await renderSignedIn();

    await navigate('/');

    expect(main.classList).toContain('app-main--wide');
    expect(main.classList).not.toContain('app-main--roomy');
  });

  it('gives main the roomy layout on a route whose data asks for it', async () => {
    const { main } = await renderSignedIn();

    await navigate('/roomy');

    expect(main.classList).toContain('app-main--roomy');
    expect(main.classList).not.toContain('app-main--wide');
  });

  it('keeps the default layout on a route without a layout value', async () => {
    const { main } = await renderSignedIn();

    await navigate('/');
    await navigate('/profile');

    expect(main.classList).not.toContain('app-main--wide');
    expect(main.classList).not.toContain('app-main--roomy');
  });

  it('declares the wide layout on the home, report and sighting detail routes among product routes', () => {
    const wide = routes
      .filter((route) => !devRoutes.includes(route))
      .filter((route) => route.data?.['layout'] === 'wide');

    expect(wide.map((route) => route.path)).toEqual(['', 'sightings/new', 'sightings/:id']);
  });
});
