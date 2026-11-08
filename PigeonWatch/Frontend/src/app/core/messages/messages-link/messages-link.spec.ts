import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MessagesLink } from './messages-link';

describe('MessagesLink', () => {
  let fixture: ComponentFixture<MessagesLink>;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    fixture = TestBed.createComponent(MessagesLink);
  });

  async function render(unreadCount?: number): Promise<HTMLAnchorElement> {
    if (unreadCount !== undefined) fixture.componentRef.setInput('unreadCount', unreadCount);

    await fixture.whenStable();

    return (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      'a.app-header__messages',
    )!;
  }

  it('links the email icon to the messages page with no badge by default', async () => {
    const link = await render();

    expect(link.getAttribute('href')).toBe('/messages');
    expect(link.getAttribute('aria-label')).toBe('Messages');
    expect(link.querySelector('app-icon[name="mail"]')).not.toBeNull();
    expect(link.querySelector('.messages-link__badge')).toBeNull();
    expect(link.textContent?.trim()).toBe('');
  });

  it('shows no badge at zero', async () => {
    const link = await render(0);

    expect(link.getAttribute('aria-label')).toBe('Messages');
    expect(link.querySelector('.messages-link__badge')).toBeNull();
  });

  it('shows the count as a hidden badge and names it in the accessible name', async () => {
    const link = await render(4);
    const badge = link.querySelector('.messages-link__badge');

    expect(badge?.textContent?.trim()).toBe('4');
    expect(badge?.getAttribute('aria-hidden')).toBe('true');
    expect(link.getAttribute('aria-label')).toBe('Messages, 4 unread');
  });

  it('keeps the tooltip as Messages while there are unread messages', async () => {
    const link = await render(2);

    link.dispatchEvent(new MouseEvent('mouseenter'));

    expect(document.body.querySelector('.pw-tooltip')?.textContent).toBe('Messages');

    link.dispatchEvent(new MouseEvent('mouseleave'));
  });

  it('drops the badge when the count goes back to zero', async () => {
    await render(3);
    const link = await render(0);

    expect(link.querySelector('.messages-link__badge')).toBeNull();
    expect(link.getAttribute('aria-label')).toBe('Messages');
  });
});
