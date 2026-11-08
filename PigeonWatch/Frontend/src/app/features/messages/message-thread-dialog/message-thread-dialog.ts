import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NavigationStart, Router } from '@angular/router';
import { filter, take } from 'rxjs';
import { SightingThread } from '../../sightings/sighting.types';
import { MessageThread } from '../message-thread/message-thread';

export interface MessageThreadDialogData {
  readonly sightingId: string;
  readonly threadId: string;
  readonly thread: SightingThread | null;
}

@Component({
  selector: 'app-message-thread-dialog',
  imports: [MessageThread],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './message-thread-dialog.html',
  styleUrl: './message-thread-dialog.scss',
})
export class MessageThreadDialog {
  private readonly dialogRef = inject<MatDialogRef<MessageThreadDialog>>(MatDialogRef);

  protected readonly data = inject<MessageThreadDialogData>(MAT_DIALOG_DATA);

  constructor() {
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationStart),
        take(1),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.close());
  }

  protected close(): void {
    this.dialogRef.close();
  }
}
