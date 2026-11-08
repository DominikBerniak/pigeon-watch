import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormField,
  FormRoot,
  form,
  maxLength,
  required,
  requiredError,
  validate,
} from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { finalize } from 'rxjs';
import { ConfigurationService } from '../../../../core/configuration/configuration.service';
import { ErrorReportingService } from '../../../../core/error-reporting/error-reporting.service';
import { ResourceService } from '../../../../core/resources/resource.service';
import { TranslatePipe } from '../../../../core/resources/translate.pipe';
import {
  Alert,
  ErrorReference,
  FieldErrors,
  FormActions,
  FormSubmitState,
  SubmitButton,
  provideFormFieldDefaults,
} from '../../../../shared/ui';
import {
  MessageFailure,
  isReportableMessageFailure,
  messageErrorMessage,
  messageFailure,
  messageFailureTraceId,
} from '../../message-errors';
import { SightingThreadStart, fallbackMessageRules } from '../../sighting.types';
import { SightingsApi } from '../../sightings-api';

export interface MessageOwnerDialogData {
  readonly sightingId: string;
  readonly ownerDisplayName: string;
}

interface MessageText {
  text: string;
}

@Component({
  providers: [provideFormFieldDefaults()],
  selector: 'app-message-owner-dialog',
  imports: [
    FormField,
    FormRoot,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    TranslatePipe,
    Alert,
    ErrorReference,
    FieldErrors,
    FormActions,
    SubmitButton,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './message-owner-dialog.html',
  styleUrl: './message-owner-dialog.scss',
})
export class MessageOwnerDialog {
  private readonly sightingsApi = inject(SightingsApi);
  private readonly configuration = inject(ConfigurationService);
  private readonly resources = inject(ResourceService);
  private readonly reporting = inject(ErrorReportingService);
  private readonly submitState = inject(FormSubmitState);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialogRef =
    inject<MatDialogRef<MessageOwnerDialog, SightingThreadStart>>(MatDialogRef);
  private readonly model = signal<MessageText>({ text: '' });
  private readonly failure = signal<MessageFailure | null>(null);

  protected readonly data = inject<MessageOwnerDialogData>(MAT_DIALOG_DATA);
  protected readonly rules = computed(
    () => this.configuration.clientConfig()?.messageRules ?? fallbackMessageRules,
  );
  protected readonly messageForm = form(this.model, (path) => {
    required(path.text);
    validate(path.text, ({ value }) => (isBlank(value()) ? requiredError() : undefined));
    maxLength(path.text, () => this.rules().textMaxLength);
  });
  protected readonly sending = signal(false);
  protected readonly textMessages = computed(() => ({
    required: this.resources.translate('sighting.messages.validation.required'),
    maxLength: this.resources.translate(
      'sighting.messages.validation.maxLength',
      this.rules().textMaxLength,
    ),
  }));
  protected readonly errorText = computed(() => {
    const failure = this.failure();

    if (!failure) return null;

    return failure.kind === 'notFound'
      ? this.resources.translate('sighting.errors.notFound')
      : messageErrorMessage(this.resources, failure);
  });
  protected readonly errorTraceId = computed(() => messageFailureTraceId(this.failure()));

  protected send(): void {
    this.submitState.markSubmitted();

    if (this.sending() || this.messageForm().invalid()) return;

    this.sending.set(true);
    this.dialogRef.disableClose = true;
    this.failure.set(null);
    this.sightingsApi
      .startThread(this.data.sightingId, this.model().text)
      .pipe(
        finalize(() => {
          this.sending.set(false);
          this.dialogRef.disableClose = false;
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (started) => this.dialogRef.close(started),
        error: (error: unknown) => this.handleFailure(error),
      });
  }

  protected cancel(): void {
    this.dialogRef.close();
  }

  private handleFailure(error: unknown): void {
    const failure = messageFailure(error);

    if (isReportableMessageFailure(error, failure)) {
      this.reporting.report('api-failure', 'sighting.threads.start', error);
    }

    this.failure.set(failure);
  }
}

function isBlank(value: string): boolean {
  return value !== '' && value.trim() === '';
}
