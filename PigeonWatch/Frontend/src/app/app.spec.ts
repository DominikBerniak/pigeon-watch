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
  { path: 'fill', component: BlankPage, data: { fill: true } },
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
    const trigger = element.querySelector<HTMLButtonElement>('header button.app-header__account');

    trigger?.click();
    await TestBed.inject(ApplicationRef).whenStable();

    return element;
  }

  function menuItems(): HTMLElement[] {
    return Array.from(document.body.querySelectorAll<HTMLElement>('[role="menuitem"]'));
  }

  it('shows a user icon button named after the user without the email', async () => {
    currentUser.set({ id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] });
    const element = await render();
    const header = element.querySelector('header');
    const trigger = header?.querySelector<HTMLButtonElement>('button.app-header__account');

    expect(header?.querySelectorAll('button').length).toBe(1);
    expect(trigger?.getAttribute('aria-label')).toBe('Account menu for Jan K');
    expect(trigger?.getAttribute('aria-haspopup')).toBe('menu');

    trigger?.dispatchEvent(new MouseEvent('mouseenter'));

    expect(document.body.querySelector('.pw-tooltip')?.textContent).toBe('Profile');

    trigger?.dispatchEvent(new MouseEvent('mouseleave'));
    expect(trigger?.textContent?.trim()).toBe('');
    expect(trigger?.querySelector('app-icon[name="user"] svg')?.getAttribute('aria-hidden')).toBe(
      'true',
    );
    expect(header?.textContent).not.toContain('Jan K');
    expect(header?.textContent).not.toContain('jan@example.com');
    expect(document.body.querySelector('.app-header__menu-name')).toBeNull();
    expect(menuItems()).toEqual([]);
  });

  it('opens a menu headed by the user name, then Profile and Log out', async () => {
    await openUserMenu();

    const menu = document.body.querySelector<HTMLElement>('[role="menu"]');
    const name = menu?.querySelector<HTMLElement>('.app-header__menu-name');
    const items = menuItems();

    expect(name?.textContent?.trim()).toBe('Jan K');
    expect(name?.getAttribute('role')).toBeNull();
    expect(name?.hasAttribute('tabindex')).toBe(false);
    expect(name?.compareDocumentPosition(items[0])).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(menu?.textContent).not.toContain('jan@example.com');
    expect(items.map((item) => item.textContent?.trim())).toEqual(['Profile', 'Log out']);
    expect(items[0].getAttribute('href')).toBe('/profile');
  });

  it('puts the report, messages and account icon buttons in that order', async () => {
    currentUser.set({ id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] });
    const element = await render();
    const user = element.querySelector('header .app-header__user');
    const controls = Array.from(user?.querySelectorAll<HTMLElement>('a, button') ?? []);

    expect(controls.length).toBe(3);
    expect(controls[0].classList).toContain('app-header__report');
    expect(controls[1].classList).toContain('app-header__messages');
    expect(controls[2].classList).toContain('app-header__account');
    expect(
      controls.map((control) => control.querySelector('app-icon')?.getAttribute('name')),
    ).toEqual(['report', 'mail', 'user']);
  });

  it('links the plus icon button to the report sighting page', async () => {
    currentUser.set({ id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] });
    const element = await render();
    const link = element.querySelector<HTMLAnchorElement>('header a.app-header__report');

    expect(link?.getAttribute('href')).toBe('/sightings/new');
    expect(link?.getAttribute('aria-label')).toBe('Report sighting');
    expect(link?.textContent?.trim()).toBe('');
    expect(link?.querySelector('app-icon[name="report"] svg')?.getAttribute('aria-hidden')).toBe(
      'true',
    );
  });

  it('links the email icon button to the messages page', async () => {
    currentUser.set({ id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] });
    const element = await render();
    const link = element.querySelector<HTMLAnchorElement>('header a.app-header__messages');

    expect(link?.getAttribute('href')).toBe('/messages');
    expect(link?.getAttribute('aria-label')).toBe('Messages');
    expect(link?.textContent?.trim()).toBe('');
    expect(link?.querySelector('app-icon[name="mail"] svg')?.getAttribute('aria-hidden')).toBe(
      'true',
    );
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

    menuItems()[1].click();

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

  it('makes main fill the viewport height on a route whose data asks for it', async () => {
    const { main } = await renderSignedIn();

    await navigate('/fill');

    expect(main.classList).toContain('app-main--fill');
  });

  it('does not make main fill the height on a route without the fill flag', async () => {
    const { main } = await renderSignedIn();

    await navigate('/fill');
    await navigate('/profile');

    expect(main.classList).not.toContain('app-main--fill');
  });

  it('declares the wide layout on the home, report, sighting list, sighting detail and messages routes among product routes', () => {
    const wide = routes
      .filter((route) => !devRoutes.includes(route))
      .filter((route) => route.data?.['layout'] === 'wide');

    expect(wide.map((route) => route.path ?? route.matcher?.name)).toEqual([
      '',
      'sightings/new',
      'sightings/list',
      'sightings/:id',
      'messagesUrlMatcher',
    ]);
  });

  it('declares no layout on any other product route', () => {
    const others = routes
      .filter((route) => !devRoutes.includes(route))
      .filter((route) => route.data?.['layout'] !== undefined && route.data['layout'] !== 'wide');

    expect(others).toEqual([]);
  });
});
