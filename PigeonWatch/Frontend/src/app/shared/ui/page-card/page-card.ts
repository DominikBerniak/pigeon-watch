import { ChangeDetectionStrategy, Component, ElementRef, input, viewChild } from '@angular/core';
import { MatCardModule } from '@angular/material/card';

export type PageCardWidth = 'form' | 'wide' | 'full';

@Component({
  selector: 'app-page-card',
  imports: [MatCardModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './page-card.html',
  styleUrl: './page-card.scss',
  host: {
    '[class.page-card-host--wide]': "width() === 'wide'",
    '[class.page-card-host--full]': "width() === 'full'",
    '[class.page-card-host--fill]': 'fillHeight()',
  },
})
export class PageCard {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
  readonly width = input<PageCardWidth>('form');
  readonly fillHeight = input(false);

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  focusHeading(): void {
    this.heading()?.nativeElement.focus();
  }
}
