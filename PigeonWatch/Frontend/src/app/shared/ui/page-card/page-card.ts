import { ChangeDetectionStrategy, Component, ElementRef, input, viewChild } from '@angular/core';
import { MatCardModule } from '@angular/material/card';

export type PageCardWidth = 'form' | 'wide';

@Component({
  selector: 'app-page-card',
  imports: [MatCardModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './page-card.html',
  styleUrl: './page-card.scss',
  host: {
    '[class.page-card-host--wide]': "width() === 'wide'",
  },
})
export class PageCard {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
  readonly width = input<PageCardWidth>('form');

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  focusHeading(): void {
    this.heading()?.nativeElement.focus();
  }
}
