import { HttpClient, HttpHeaders, HttpClientModule } from '@angular/common/http';
import { Component, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { default as SparkMD5 } from 'spark-md5';

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrls: ['./app.css'],
  standalone: true,
  imports: [CommonModule, FormsModule, HttpClientModule] 
})
export class AppComponent {
  chunkSizeMB: number = 8; 
  maxParallelChunks: number = 4;
  maxRetries: number = 3; 

  selectedFiles: File[] = [];
  uploadStatus: string = '';
  isUploading: boolean = false;

  constructor(private http: HttpClient, private cdr: ChangeDetectorRef) {}

  get chunkSizeBytes(): number {
    return this.chunkSizeMB * 1024 * 1024;
  }

  onFileSelected(event: any): void {
    const target = event.target as HTMLInputElement;
    if (target.files && target.files.length > 0) {
      this.selectedFiles = Array.from(target.files);
      this.uploadStatus = `${this.selectedFiles.length} file(s) selected ready to upload.`;
    }
  }

  async onUpload(): Promise<void> {
    if (this.selectedFiles.length === 0) return;

    this.isUploading = true;
    this.uploadStatus = 'Calculating master MD5 signatures...';
    this.cdr.detectChanges();

    try {
      const metas = await this.hashAll(this.selectedFiles);
      this.uploadStatus = 'Initializing remote transfer batch sessions...';
      this.cdr.detectChanges();

      this.http.post<any>('/api/uploads/batches', { files: metas })
        .subscribe({
          next: async (batch) => {
            this.uploadStatus = 'Streaming chunks with connection tracking monitoring...';
            this.cdr.detectChanges();

            const jobs = batch.files.map((info: any) => {
              const matchingFile = this.selectedFiles.find(f => f.name === info.fileName);
              return matchingFile ? this.uploadOne(matchingFile, info) : Promise.resolve();
            });

            try {
              await Promise.all(jobs);

              this.uploadStatus = 'Stitching data fragments on storage cluster node...';
              this.cdr.detectChanges();

              this.http.post(`/api/uploads/batches/${batch.batchId}/complete`, {}, { responseType: 'text' })
                .subscribe({
                  next: (response) => {
                    this.uploadStatus = 'Batch completed and signature matches verified successfully!';
                    this.selectedFiles = [];
                    this.isUploading = false;
                    this.cdr.detectChanges();
                  },
                  error: (err) => this.handleError(err)
                });

            } catch (chunkError: any) {
              this.uploadStatus = `Critical fault: ${chunkError.message || 'Chunk transmission pipeline crashed.'}`;
              this.isUploading = false;
              this.cdr.detectChanges();
            }
          },
          error: (err) => this.handleError(err)
        });

    } catch (hashError) {
      this.handleError(hashError);
    }
  }

  private hashAll(files: File[]): Promise<any[]> {
    const promises = files.map(async (file) => {
      const md5Hex = await this.md5File(file);
      return {
        fileName: file.name,
        sizeBytes: file.size,
        md5Hex: md5Hex,
        chunkSizeBytes: this.chunkSizeBytes
      };
    });
    return Promise.all(promises);
  }

  private uploadOne(file: File, info: any): Promise<void> {
    return new Promise((resolve, reject) => {
      let nextIndex = 0;
      let active = 0;
      let failed = false;

      const pump = () => {
        if (failed) return;

        while (active < info.maxParallelChunks && nextIndex < info.chunkCount) {
          const currentIndex = nextIndex++;
          active++;

          this.putChunkWithRetry(file, info.fieldId, currentIndex, 0).then(
            () => {
              active--;
              if (nextIndex >= info.chunkCount && active === 0) {
                resolve();
              } else {
                pump();
              }
            },
            (err) => {
              failed = true;
              reject(new Error(`Chunk ${currentIndex} for file ${file.name} failed after ${this.maxRetries} retries.`));
            }
          );
        }
      };

      pump();
    });
  }

  private putChunkWithRetry(file: File, fileId: string, index: number, attempt: number): Promise<any> {
    const start = index * this.chunkSizeBytes;
    const blob = file.slice(start, Math.min(start + this.chunkSizeBytes, file.size));

    return this.executeChunkPut(blob, fileId, index).catch((error) => {
      if (attempt < this.maxRetries) {
        const delayMs = Math.pow(2, attempt) * 1000;
        console.warn(`[Retry Alert] Chunk ${index} failed. Attempt ${attempt + 1}/${this.maxRetries}. Retrying in ${delayMs}ms...`);
        
        return new Promise(res => setTimeout(res, delayMs))
          .then(() => this.putChunkWithRetry(file, fileId, index, attempt + 1));
      }
      throw error; 
    });
  }

  private async executeChunkPut(blob: Blob, fileId: string, index: number): Promise<any> {
    const md5B64 = await this.md5BlobBase64(blob);
    const headers = new HttpHeaders({
      'Content-Type': 'application/octet-stream',
      'Content-MD5': md5B64
    });
    return this.http.put(`/api/uploads/files/${fileId}/chunks/${index}`, blob, { headers, responseType: 'text' }).toPromise();
  }

  private md5File(file: File): Promise<string> {
    return new Promise((resolve, reject) => {
      const spark = new SparkMD5.ArrayBuffer();
      let offset = 0;
      const reader = new FileReader();
      
      reader.onload = (e: any) => {
        spark.append(e.target.result);
        offset += this.chunkSizeBytes;
        next();
      };
      reader.onerror = (err) => reject(err);

      const next = () => {
        if (offset >= file.size) {
          resolve(spark.end());
          return;
        }
        const blob = file.slice(offset, Math.min(offset + this.chunkSizeBytes, file.size));
        reader.readAsArrayBuffer(blob);
      };
      next();
    });
  }

  private md5BlobBase64(blob: Blob): Promise<string> {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = (e: any) => {
        const hash = SparkMD5.ArrayBuffer.hash(e.target.result);
        const bytes = new Uint8Array(hash.length / 2);
        for (let i = 0; i < hash.length; i += 2) {
          bytes[i / 2] = parseInt(hash.substring(i, i + 2), 16);
        }
        let binary = '';
        bytes.forEach(b => binary += String.fromCharCode(b));
        resolve(btoa(binary));
      };
      reader.onerror = (err) => reject(err);
      reader.readAsArrayBuffer(blob);
    });
  }

  private handleError(error: any): void {
    console.error(error);
    this.uploadStatus = 'An unhandled pipeline error disrupted the processing steps.';
    this.isUploading = false;
    this.cdr.detectChanges();
  }
}
