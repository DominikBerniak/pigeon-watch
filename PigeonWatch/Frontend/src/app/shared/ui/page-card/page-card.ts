import { ChangeDetectionStrategy, Component, ElementRef, input, viewChild } from '@angular/core';
import { MatCardModule } from '@angular/material/card';

@Component({
  selector: 'app-page-card',
  imports: [MatCardModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './page-card.html',
  styleUrl: './page-card.scss',
})
export class PageCard {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  focusHeading(): void {
    this.heading()?.nativeElement.focus();
  }
}
