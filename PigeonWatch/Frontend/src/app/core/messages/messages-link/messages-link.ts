import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { Icon } from '../../../shared/ui/icon/icon';
import { Tooltip } from '../../../shared/ui/tooltip/tooltip';
import { TranslatePipe } from '../../resources/translate.pipe';

@Component({
  selector: 'app-messages-link',
  imports: [Icon, MatButtonModule, RouterLink, Tooltip, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './messages-link.html',
  styleUrl: './messages-link.scss',
})
export class MessagesLink {
  readonly unreadCount = input(0);
}
