import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslatePipe } from '../../../core/resources/translate.pipe';

@Component({
  selector: 'app-home-page',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './home-page.html',
})
export class HomePage {}
